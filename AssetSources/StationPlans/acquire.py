"""Download public station platform CSVs and operator plan archives. No credentials."""
import requests, pathlib, re, json, concurrent.futures, hashlib, time
HERE=pathlib.Path(__file__).resolve().parent;CACHE=HERE/'cache';CACHE.mkdir(exist_ok=True)

def retrieve(i):
 try:
  meta_path=CACHE/f'{i}.json'
  if not meta_path.exists():
   r=requests.get(f'https://www.data.go.kr/catalog/{i}/fileData.json',timeout=30);r.raise_for_status();meta_path.write_bytes(r.content)
  meta=json.loads(meta_path.read_text())
  if '승강장_정보' not in meta.get('name',''):return None
  hp=CACHE/f'{i}.html'
  if not hp.exists(): hp.write_bytes(requests.get(f'https://www.data.go.kr/data/{i}/fileData.do',timeout=30).content)
  a=re.findall(r"fn_fileDataDown\('([^']*)', '([^']*)', '([^']*)','([^']*)', '([^']*)'\)",hp.read_text())[0]
  dp=CACHE/f'{i}-download.json'
  if not dp.exists():
   r=requests.get('https://www.data.go.kr/tcs/dss/selectFileDataDownload.do',params=dict(publicDataPk=a[0],publicDataDetailPk=a[1],atchFileId=a[2],fileDetailSn=a[3],publicDataTyCode='PR0051'),timeout=30);r.raise_for_status();dp.write_bytes(r.content)
  data=json.loads(dp.read_text());assert data.get('status')
  params=dict(atchFileId=data['atchFileId'],fileDetailSn=data['fileDetailSn'],insertDataPrcus='N')
  url=requests.Request('GET','https://www.data.go.kr/cmm/cmm/fileDownload.do',params=params).prepare().url
  path=HERE/f'{i}.csv'
  if not path.exists():
   r=requests.get(url,timeout=45);r.raise_for_status();path.write_bytes(r.content)
  result=dict(id=str(i),name=meta['name'],source=meta['url'],downloadURL=url,modified=meta.get('dateModified'),published=meta.get('datePublished'),license=meta.get('license'),file=path.name,sha256=hashlib.sha256(path.read_bytes()).hexdigest(),bytes=path.stat().st_size)
  print(result['name'],result['bytes'],flush=True);return result
 except Exception as e:print(i,'ERROR',str(e),flush=True);return dict(id=str(i),error=str(e))

if __name__=='__main__':
 with concurrent.futures.ThreadPoolExecutor(max_workers=3) as executor:
  rows=[r for r in executor.map(retrieve,range(15041169,15041210)) if r]
 (HERE/'sources.json').write_text(json.dumps(dict(retrieved='2026-10-08',rows=rows),ensure_ascii=False,indent=2))
