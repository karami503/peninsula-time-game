# 반도의 시간 온라인 서버

온라인 대전(한반도 땅따먹기)과 힐링 온라인(도시 공개·구경)을 처리하는 서버입니다. Node.js 20 이상이 필요하며 다른 패키지는 쓰지 않습니다.

```bash
cd OnlineServer
npm test      # 규칙·HTTP 테스트
npm start     # http://0.0.0.0:8787
```

환경 변수:

- `PORT`: 기본 `8787`.
- `HOST`: 기본 `0.0.0.0`(같은 네트워크의 다른 기기도 접속 가능). 이 컴퓨터에서만 쓰려면 `127.0.0.1`.
- `DATA_DIR`: 계정·공개 도시·진행 중인 대전을 저장할 폴더. 기본 `OnlineServer/data`. 대전은 10초마다, 그리고 서버를 끌 때(Ctrl+C, SIGTERM) 저장되어 다시 켜면 이어집니다.

게임의 온라인 화면(`F7`)에서 서버 주소를 입력합니다. 같은 와이파이에서는 `http://<서버 컴퓨터의 내부 IP>:8787`을 씁니다. 인터넷으로 공개하려면 서버를 호스팅에 올리고 HTTPS 리버스 프록시 뒤에 두세요.

## 구조

- `battle.js`: 대전 규칙. 0.5초마다 진행합니다. 병력·금 증가, 공격(빈 땅 칸당 병력 2, 적 땅은 상대의 병력 밀도·기술·요새에 따라 결정), 공격·방어·경제 기술, 요새·병영, AI 세력, 승리 판정이 들어 있습니다.
- `accounts.js`: 계정. 비밀번호는 scrypt(무작위 솔트)로 해시하고, 로그인 세션 토큰은 SHA-256 해시로만 저장합니다(30일). 같은 아이디로 5번 틀리면 1분 동안 로그인을 막습니다. 없는 아이디도 같은 시간이 걸리게 해시를 계산합니다.
- `healing.js`: 공개 도시 저장. 계정당 한 도시이며 다시 공개하면 바뀝니다. 30일 동안 갱신이 없으면 지우고, 최대 2,000개까지 보관합니다.
- `server.js`: HTTP API.
- `korea-grid.txt`: 96×128 한반도 육지 격자. `AssetSources/Geography/make_korea_grid.py`로 다시 만듭니다.

## API

| 메서드 | 경로 | 설명 |
|---|---|---|
| GET | `/health` | 상태 |
| POST | `/auth/register` `{username, password}` | 회원가입. `{token, username}` |
| POST | `/auth/login` `{username, password}` | 로그인. `{token, username}` |
| GET | `/auth/me` | 현재 로그인 계정 (`Authorization: Bearer <token>`) |
| POST | `/auth/logout` | 로그아웃 |
| GET | `/battle/rooms` | 진행 중인 대전 목록 |
| POST | `/battle/join` | 로그인 필요. 하던 대전이 있으면 그 방으로, 없으면 배치 중인 방이나 새 방으로. `{room, token, index}` |
| GET | `/battle/state?room=&token=` | 방 상태. 토큰이 없으면 관전 |
| POST | `/battle/act` `{room, token, type, tile, kind, ratio}` | `spawn`, `attack`, `tech`(`attack`/`defense`/`economy`), `build`(`fort`/`barracks`) |
| GET | `/healing/list` | 공개 도시 목록(최근 100개) |
| GET | `/healing/city?id=` | 공개 도시 하나(읽기 전용) |
| POST | `/healing/publish` | 로그인 필요. 내 도시 공개·갱신 |

토큰 없이 명령하면 403이 반환됩니다. 요청 본문은 16KB로 제한됩니다. IP마다 초당 약 25회로 요청을 제한합니다. 이름에서 제어 문자와 `<>`를 지웁니다. 도시 코드·건물 코드·도로 범위와 각종 수치를 검사합니다.

## 한계

비밀번호 찾기·이메일 인증은 없습니다. 서버가 갑자기 꺼지면(전원 차단 등) 마지막 저장 이후 최대 10초의 대전 진행이 사라집니다. 인터넷에 공개할 때는 반드시 HTTPS 뒤에 두세요. HTTP로는 비밀번호가 암호화되지 않습니다.
