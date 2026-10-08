import requests,pathlib,json,re,concurrent.futures,hashlib
from html import unescape
HERE=pathlib.Path(__file__).resolve().parent;cache=HERE/'cache';images=cache/'busan-plans';images.mkdir(exist_ok=True)
base='https://www.humetro.busan.kr'
stations=[]
for line in range(1,5):
 r=requests.get(base+'/homepage/stationinfo/stationCodeAjax.do',params={'s_line':line},timeout=30)
 (cache/f'busan-stations-{line}.html').write_bytes(r.content)
 for code,name in re.findall(r'<option[^>]*value="([0-9]+)"[^>]*>(.*?)</option>',r.text,re.S):
  stations.append(dict(line=line,code=code,name=unescape(name).strip()))

def get(s):
 try:
  url=base+'/homepage/default/stationinfo/page/list01.do?menu_no=1001010201&s_line='+str(s['line'])+'&s_station='+s['code']
  path=cache/f'busan-info-{s["code"]}.html'
  if not path.exists():path.write_bytes(requests.get(url,timeout=30).content)
  links=re.findall(r'<a[^>]*href="([^"]+)"[^>]*>.*?역안내도 다운로드.*?</a>',path.read_text(),re.S)
  links=re.findall(r'href="([^"]*DownloadServlet[^"]*)"',path.read_text())
  link=links[0] if links else None
  s['source']=url;s['planURL']=base+unescape(link) if link else ''
  if link:
   f=images/(s['code']+'.gif')
   if not f.exists():f.write_bytes(requests.get(s['planURL'],timeout=30).content)
   s.update(file=str(f.relative_to(HERE)),sha256=hashlib.sha256(f.read_bytes()).hexdigest(),bytes=f.stat().st_size)
  print(s['line'],s['name'],s.get('bytes',0),flush=True);return s
 except Exception as e:s['error']=str(e);return s
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as ex: rows=list(ex.map(get,stations))
(HERE/'busan-plans.json').write_text(json.dumps(dict(source=base,retrieved='2026-10-08',license='Operator website; retain attribution; originals are reference material',rows=rows),ensure_ascii=False,indent=2))
