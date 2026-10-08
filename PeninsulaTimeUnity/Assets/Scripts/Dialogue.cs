using System.Collections.Generic;

namespace PeninsulaTime
{
    // What people say when the player talks to them: lines for who they are, where they are (street, station,
    // shop, bus, train), day or night, and how the city is doing. Recently said lines are not repeated.
    public static class Dialogue
    {
        public static readonly string[] Personas={"직장인","대학생","고등학생","어르신","관광객","배달 기사","주민","아이 부모","러너","상인","커플","유학생"};
        static readonly string[][] ByPersona={
            new[]{"점심 뭐 먹을지 고민하는 게 하루 중 제일 어려워요.","오늘도 야근 확정이에요. 커피 한 잔 더 해야겠네요.","회의가 세 개나 남았어요. 잠깐 바람 쐬러 나왔어요.","출근길 지하철은 정말 전쟁이에요.","월급날까지 일주일… 버텨야죠.","퇴근하고 헬스장 가려고요. 마음만은요.","팀장님한테 보고서 넘기고 나왔어요. 후련하네요.","재택근무가 그리워요."},
            new[]{"과제 마감이 내일이라 카페 가는 중이에요.","시험 기간이라 도서관 자리 잡으러 가요.","동아리 공연 연습하러 가요. 보러 오세요!","학식이 오늘 좀 괜찮았어요.","알바 끝나고 친구들 만나기로 했어요.","교환학생 준비 중이라 정신이 없어요.","전공 수업이 생각보다 재밌어요.","통학이 한 시간 반이라 지하철에서 책 읽어요."},
            new[]{"학원 가는 길이에요. 오늘 수학이라 싫어요.","편의점에서 컵라면 먹고 가려고요.","수행평가 때문에 친구들이랑 모여요.","체육대회 연습하느라 다리가 아파요.","노래방 갔다가 들어가려고요. 비밀이에요.","방학 언제 오죠?"},
            new[]{"젊었을 땐 이 동네가 다 논밭이었어.","요즘 버스 정류장 안내판이 참 친절하더구먼.","손주 보러 가는 길이야.","무릎이 시원찮아서 천천히 걸어.","경로당에서 바둑 한 판 두고 오는 길이지.","지하철 계단이 좀 줄었으면 좋겠어.","날이 좋으니 공원 한 바퀴 돌아야지."},
            new[]{"지도 앱 없이도 길 찾기 쉬운 도시네요.","여기 맛집 추천해 주실 수 있어요?","지하철 노선이 많아서 처음엔 헷갈렸어요.","야경이 정말 예쁘다고 들었어요.","기념품 사러 시장에 가 보려고요.","버스랑 지하철을 바로 갈아탈 수 있어서 편하네요.","사진 찍기 좋은 곳이 너무 많아요."},
            new[]{"주문이 밀려서 잠깐만요, 금방 가야 해요.","이 골목은 일방통행이라 돌아가야 해요.","비 오는 날은 콜이 두 배예요.","오토바이 세워 둘 곳이 없어서 늘 고민이에요.","오늘만 벌써 서른 건째예요."},
            new[]{"장 보러 가는 길이에요. 채소값이 또 올랐어요.","아파트 단지 앞에 공원이 생겨서 좋아요.","저녁 반찬 뭐 할지 아직 못 정했어요.","동네 도서관 프로그램이 알차요.","택배가 오늘 온다는데 집에 가 봐야겠어요."},
            new[]{"애가 킥보드 타겠다고 해서 나왔어요.","어린이집 하원 시간이라 서둘러야 해요.","놀이터가 새로 생겨서 매일 와요.","유모차 끌고 다니기 편한 길이 많아졌어요.","주말에 키즈카페 가기로 약속했어요."},
            new[]{"한 바퀴 5킬로미터 코스예요. 같이 뛰실래요?","저녁에 뛰면 시원해서 좋아요.","무릎 보호대 필수예요.","오늘 기록 단축했어요!","천변 산책로가 달리기 제일 좋아요."},
            new[]{"오늘 장사가 좀 되네요.","월세가 또 오른다고 해서 걱정이에요.","단골손님이 많아서 버티는 거예요.","새로 연 가게들 때문에 경쟁이 세졌어요.","요즘은 포장 손님이 더 많아요."},
            new[]{"오늘 우리 100일이에요!","영화 보고 밥 먹으러 가요.","사진 좀 찍어 주실 수 있어요?","어디 분위기 좋은 카페 아세요?","둘 다 길치라서 계속 헤매는 중이에요."},
            new[]{"한국어 아직 공부 중이에요. 천천히 말해 주세요.","떡볶이가 제일 좋아요. 조금 매워요.","기숙사 통금 때문에 일찍 들어가야 해요.","한국 겨울은 정말 추워요.","지하철 도착 방송 멜로디가 기억에 남아요."}};
        static readonly Dictionary<string,string[]> ByPlace=new Dictionary<string,string[]>{
            {"gangnam",new[]{"강남대로는 언제 와도 차가 많아요.","이 근처 빌딩들은 밤에도 불이 켜져 있어요.","중앙버스차로 덕분에 버스가 빨라졌어요.","점심시간엔 식당마다 줄이 길어요.","학원가가 가까워서 저녁엔 학생들로 붐벼요."}},
            {"hongdae",new[]{"주말 밤엔 버스킹 공연이 많아요.","골목마다 개성 있는 가게가 있어요.","클럽 거리는 밤이 돼야 시작이에요.","걷고 싶은 거리에서 공연 보다가 왔어요.","공항철도 타면 공항까지 금방이에요."}},
            {"seoulstation",new[]{"KTX 시간 맞추려고 뛰어왔어요.","열차가 자주 있어서 편해요.","서울역 광장은 늘 사람으로 가득해요.","고가 산책로에서 보는 기찻길이 멋있어요.","출장 가는 길이에요. 도시락 사야겠어요."}},
            {"gimpo",new[]{"제주 가는 비행기 타러 왔어요.","공항철도 타고 오면 금방이에요.","출국 전에 짐 무게부터 확인해야겠어요.","비행기 지연이 없어야 할 텐데요."}},
            {"station",new[]{"다음 열차까지 얼마나 남았나 보고 있었어요.","스크린도어 덕분에 승강장이 안전해졌죠.","환승 통로가 너무 길어요.","바닥 안내선을 따라가면 환승할 수 있어요.","출구 번호만 잘 기억하면 안 헤매요.","지하상가에서 구경하다 열차 놓칠 뻔했어요."}},
            {"shop",new[]{"여기 신상품 들어왔대요.","계산 줄이 기네요.","1+1 행사 하는 거 보셨어요?","이 가게 사장님이 친절해요."}},
            {"arcade",new[]{"지하상가는 비 오는 날 걷기 좋아요.","옷값이 위보다 싸요.","가게가 워낙 많아서 길을 잃어요.","액세서리 구경하는 재미가 있어요."}},
            {"metro",new[]{"이번 역에서 내려야 하는데 자리 양보할게요.","출근 시간엔 이 칸이 제일 덜 붐벼요.","노선도 보면서 갈아탈 역 확인 중이에요.","창밖은 깜깜한데 왠지 계속 보게 돼요."}},
            {"bus",new[]{"하차벨 누르는 걸 자꾸 깜빡해요.","다음 정류장이 어디더라.","버스 안에서 졸다가 종점까지 간 적 있어요.","문이 열리면 천천히 내리세요."}},
            {"ktx",new[]{"창가 자리라 경치 보면서 가요.","도시락 먹으면서 가는 게 기차 여행의 맛이죠.","터널을 지날 때마다 귀가 먹먹해요.","도착까지 한숨 자야겠어요."}},
            {"terminal",new[]{"체크인 카운터가 어디죠?","보안검색 줄이 생각보다 짧네요.","탑승구가 멀어서 서둘러야 해요."}}};
        static readonly string[] Night={"밤바람이 시원하네요.","가로등 켜지니까 거리가 예뻐요.","막차 놓치면 안 되는데…","야식 먹으러 가는 길이에요.","밤에는 조심히 다니세요.","이 시간엔 택시 잡기가 어려워요.","별이 잘 안 보여요. 도시라서 그렇겠죠."};
        static readonly string[] Day={"햇볕이 따뜻하네요.","오늘 하늘 정말 맑아요.","점심 먹고 산책 중이에요.","낮엔 길이 막혀서 지하철이 최고예요."};
        static readonly string[] Unhappy={"세금이 너무 비싸요.","집값 때문에 이사를 고민 중이에요.","도시가 너무 붐벼요.","버스가 자주 안 와서 불편해요.","공원이 좀 더 있었으면 좋겠어요."};
        static readonly string[] Fine={"그럭저럭 살 만한 동네예요.","교통이 조금만 더 좋아지면 좋겠어요.","요즘 동네가 조금씩 바뀌고 있어요."};
        static readonly string[] Happy={"이 도시에 살아서 행복해요!","시장님 덕분에 살기 좋아졌어요.","길이 깨끗하고 편해요.","어디든 대중교통으로 갈 수 있어서 좋아요."};
        static readonly string[] Again={"또 만났네요!","아까 그분이시죠? 반가워요.","오늘 자주 마주치네요.","저 이제 진짜 가 봐야 해요.","아직 이 근처 구경 중이세요?"};
        static readonly Queue<string> recent=new Queue<string>();static readonly HashSet<string> recentSet=new HashSet<string>();
        const int Memory=40;

        public static string Line(int persona,int talks,int happiness,string place,bool night,System.Random random)
        {
            var pool=new List<string>();
            pool.AddRange(ByPersona[persona%ByPersona.Length]);
            string[] here;if(place!=null&&ByPlace.TryGetValue(place,out here)){pool.AddRange(here);pool.AddRange(here);}
            pool.AddRange(night?Night:Day);
            pool.AddRange(happiness<40?Unhappy:happiness<70?Fine:Happy);
            if(talks>0)pool.AddRange(Again);
            var fresh=pool.FindAll(l=>!recentSet.Contains(l));
            if(fresh.Count==0)
            {
                // Expire oldest speech until this person's pool has a fresh line.
                while(fresh.Count==0&&recent.Count>0)
                {
                    var oldest=recent.Dequeue();recentSet.Remove(oldest);
                    fresh=pool.FindAll(l=>!recentSet.Contains(l));
                }
                if(fresh.Count==0)fresh=pool;
            }
            var line=fresh[random.Next(fresh.Count)];
            recent.Enqueue(line);recentSet.Add(line);
            while(recent.Count>Memory)recentSet.Remove(recent.Dequeue());
            return line;
        }
    }
}
