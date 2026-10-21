using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//1.설정과 상태
//2.준비 Awake부품 처음위치를 기억  -> Start (IK 준비를 기다린뒤 대기 위치로)
//3.부품 찾기
//4.집어서 옮기기
//5.테스트 : 부품 원래위치로 되돌리기, OnGUI
public class PickPlaceSequence : MonoBehaviour
{
    #region 0.설정과 상태

    [Header("필수 연결")]
    public ArmIK ik;
    public GripperController gripper; 
    public Transform spawnPoint; // 부품 리셋 위치
    public Transform[] bins; // 부품을 넣을 통들. index고른다. // 0 빨간통 1 파란통

    [Header("위치")]
    public Vector3 homePosition = new Vector3(0.1f, 0.22f, -0.18f); //대기 위치 (작업이 끝나면 돌아오는곳)
    public float approachHeight = 0.1f; // 부품 중심 위 몇 m에서 내려갈지
    public float pickHeightOffset = 0.006f; // 손가락이 테이블에 닿으면 올리기
    public float binDropHeight = 0.12f; // 통 바닥 기준 놓는 높이

    [Header("속도")]
    public float travelSpeed = 0.15f; //통으로 옮길때 속도
    public float approachSpeed = 0.05f; //부품쪽으로 내려가고 올라올때 이동속도

    [Header("테스트")]
    public bool showTestButtons = true; // 화면에 테스트 버튼 표시여부
    public float[] yawOptions = { 0, 15, 30, 45, 60, 75, 90, 105, 120, 135, 150, 165 }; // 15도 간격 12칸

    public bool IsBusy { get; private set; }    //작업중인지 (작업중에는 새 명령을 받지 않기위해서)
    public string Status { get; private set; } = "대기"; //지금 단계 (화면 표시용)
    public Rigidbody LastPart { get; private set; }  //마지막으로 집으려한 부품
    public bool LastSuccess { get; private set; }    //마지막 시도에서 잡기 성공했는지

    private int selectedYaw; //테스트 버튼에서 고른 손목 각도 칸의 번호 (yawOptions 의 몇번째인지)

    Dictionary<Rigidbody, (Vector3 pos, Quaternion rot)> initialPoses = new Dictionary<Rigidbody, (Vector3 pos, Quaternion rot)>();

    #endregion

    #region 1.준비

    private void Awake()
    {
        // Arm IK 가 null 이라면 ik 연결해주기
        if(ik == null)
        {
            ik = GetComponent<ArmIK>();
        }

        // 반복문으로 돌면서 현재씬에 있는 "Part" 라고 하는 부품 전부 찾기 GameObject.FindGameObjectsWithTag
        foreach (GameObject go in GameObject.FindGameObjectsWithTag("Part"))
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();// 부품에 붙어있는 Rigidbody를 변수로 저장
            if (rb != null)
            {
                initialPoses[rb] = (rb.transform.position, rb.transform.rotation);// Rigidbody가 존재하면 initialPoses 딕셔너리에 값을 저장한다
            }
        }

        // 코루틴
        IEnumerator Start()
        {
            IsBusy = true;
            yield return new WaitUntil(() => ik.Ready); //ik 사용을위해서 ik준비 완료때까지 기다린다
            Status = "대기 위치로";
            yield return ik.MoveTo(homePosition, 0f); //손목 0도로 대기 위치까지 이동
            Status = "대기";
            IsBusy = false;
        }

    }

    #endregion

    #region 2. 부품 찾기
    //Spawn point에서 가장 가까운 부품을 찾는다
    //부품을 하나씩 보면서 거리를 재고, 지금까지 가장 가까운 것보다 가까우면 바꾼다.
    private Rigidbody FindPart()
    {
        Rigidbody best = null; //가장 가까운 부품
        float bestDist = float.MaxValue; //아주 큰 값으로 시작해야 첫 부품이 무조건 뽑힘

        //기준점: SpawnPoint, 없으면 로봇 위치
        Vector3 from = spawnPoint != null ? spawnPoint.position : transform.position;

        foreach (GameObject go in GameObject.FindGameObjectsWithTag("Part"))
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null || rb == gripper.HeldPart) continue; //Rigidbody 없거나 이미 잡고있으면 건너뜀

            float d = Vector3.Distance(rb.position, from); //기준점과 부품 사이의 거리

            // 미션1
            // 더 가까운 거리를 찾았다면 변수 값 변경
            if (d < bestDist) // 더 가까우면 1등을 교체
            {
                bestDist = d;
                best = rb;
            }
        }

        // 미션2
        // 가장 가까운 부품 리턴해주기 
        return best; // 가장 가까운 부품을 돌려줌
    }

    #endregion


    #region 3. 집어서 옮기기

    // binIndex 어느통에 넣을지
    // graspYaw 몇도록 잡을지
    // graspOnly = ture 면 학습용 / false = 시연용
    // 학습용 (ML)은 부품을 잡고 들어올리기 까지 진행
    // 시연용 (Unity play) 은 통에 넣는 것 까지 진행

    // target = 잡을 부품 (null인 경우에는 spawnpoint에서 가장 가까운 부품)
    // 동작순서
    // 부품 위 -> 내려가기 -> 잡기(판정) -> 들어올리기 -> 통위 -> 놓기 -> 대기위치로 복귀
    public IEnumerator Run(int binIndex, float graspYaw, bool graspOnly = false, Rigidbody target = null)
    {
        if (IsBusy) yield break;   //이미 작업 진행중이면 무시

        IsBusy = true;
        LastSuccess = false; //지난번 결과 지우기

        //잡을 부품 정하기, 전달받은 target이 있다면 사용하고 아니면 가장 가까운 부품 찾음
        Rigidbody part = target != null ? target : FindPart();
        LastPart = part;
        if(part == null)
        {
            Status = "부품 없음";
            IsBusy = false; 
            yield break;
        }

        //사람이 컵을 집는다
        //1.손을 컵 위쪽으로 가져간다(above)
        //2.수직으로 내려간다 (pick)
        //3.잡는다
        //4.다시 들어올린다 (above)

        Vector3 p = part.position;   //부품 중심

        //Vector3.up은 (0,1,0) 즉 위쪽으로 1m인 방향
        Vector3 above = p + Vector3.up * approachHeight; // 부품 바로 위 10cm

        //그리퍼가 부품을 잡을때 손가락 끝이 테이블에 먼저 닿을수 있으므로 부품에서 PickHeightOffset을 적용해야함
        Vector3 pick = p + Vector3.up * pickHeightOffset; //잡는 위치는 PickHeightOffset 만큼 올라감.

        //1.부품 위로: 큰 이동 MoveTo
        Status = $"1. 부품 위로 (손목{graspYaw}도)";
        // above = 이동할 곳 지정
        // graspYaw = 움직일 각도
        yield return ik.MoveTo(above, graspYaw);

        //2.내려가기: 잡는 위치로 내려간다. 부품을 건드리면 안되니 MoveLinear 수직 직선 이동
        Status = "2.내려가기";
        yield return ik.MoveLinear(pick, approachSpeed, graspYaw);

        //3. 잡기 : 그리퍼가 닫기 전에 판정 + 닫기 + 붙이기까지 한다
        Status = "3.잡기";
        yield return gripper.Close();

        //4.잡기 실패
        // HeldPart = 지금 잡고 있는 부품 
        if (gripper.HeldPart == null) //판정에서 실패했다면 떨어졌거나 손가락이 제대로 닿지않은경우
        {
            //LastGraspInfo 를 통해서 실패 이유를 알 수 있다 
            Status = $"잡기 실패 - {gripper.LastGraspInfo}"; //실패 이유를 그리퍼에서 받아 표시

            // 미션1
            // 그리퍼 들어올리기 = 수직이동을 한다는 뜻이다
            yield return ik.MoveLinear(above, approachSpeed, graspYaw); //들어 올림

            // 미션2
            // 그리퍼 열기
            yield return gripper.Open();

            // 미션3
            // 학습용 모드인지 아닌지체크해서, 학습용이 아니라면 다시 대기상태로 이동
            if (!graspOnly)
            {
                yield return ik.MoveTo(homePosition, 0f);
            }
            IsBusy = false;
            yield break;
        }

        //5. 들어올리기
        Status = "4.들어올리기";
        yield return ik.MoveLinear(above, approachSpeed, graspYaw);

        //들어올린 뒤에도 잡고있으면 성공
        LastSuccess  = gripper.HeldPart != null;

        //학습용이면 여기서 끝 (성공 여부만 확인하고 부품을 놓음)
        if (graspOnly)
        {
            Status = LastSuccess ? "학습: 잡기 성공" : "학습 : 잡기 실패";
            yield return gripper.Open();
            IsBusy = false;
            yield break ;
        }

        //6.통 위로
        Status = $"5.{bins[binIndex].name}위로";
        //bins[binIndex] 0이면 빨강통, 1이면 파란통
        //Vector3.up * binDropHeight  는 위쪽으로 0.12m
        //통바닥을 기준으로 잡으면 그리퍼랑 부품이 벽에 부딪힘. 충분한 높이에서 떨어지면서 통으로 들어가게 만들어야함.
        Vector3 binAbove = bins[binIndex].position + Vector3.up * binDropHeight;
        yield return ik.MoveLinear(binAbove, travelSpeed, graspYaw);

        //7.놓기
        Status = "6.놓기";
        yield return gripper.Open();

        //8.복귀 (대기 위치로)
        Status = "7.복귀";
        yield return ik.MoveTo(homePosition, 0f);

        Status = "완료";
        IsBusy = false;
    }

    #endregion


    #region 테스트
    //부품을 ply 시작때의 위치,회전으로 되돌리기
    //잡고있는 부품은 건드리지않는다.
    private void ResetParts()
    {
        foreach (var kv in initialPoses)
        {
            Rigidbody rb = kv.Key;
            if (rb == null || rb == gripper.HeldPart) continue;

            rb.transform.SetParent(null, true); //혹시 다른 물체의 자식이면 떼어냄
            rb.isKinematic = false; // 물리 다시 켜기
            rb.linearVelocity = Vector3.zero; // 움직이던 속도 없애기
            rb.angularVelocity = Vector3.zero;  //돌던 속도 없애기
            rb.transform.SetPositionAndRotation(kv.Value.pos, kv.Value.rot);
        }

        Physics.SyncTransforms();    //옮긴 위치를 물리엔진에 바로 반영
        Status = "부품 리셋 완료(처음 위치,회전값으로)";
    }

    //화면에 테스트 패널
    //상태 정보 확인, 손목 각도 고르기, Run 실행, 부품 리셋 버튼
    //OnGUI는 매 프레임 마다 실행되어서 화면을 다시 그린다
    private void OnGUI()
    {
        //테스트 버튼 표시가 꺼져있으면 화면 그리지 않음
        if (!showTestButtons)
        {
            return;
        }

        //상자 영역 만들기
        //기준점(0,0)에서 화면 왼쪽 위. x는 오른쪽으로 y는 아래커짐
        //왼쪽에서 10, 위에서 215 정도 내려온 곳에 너비 300 높이 230 상자를 만든다
        GUILayout.BeginArea(new Rect(10, 215, 300, 230), GUI.skin.box);
        GUILayout.Label($"상태: {Status}");

        //아래 버튼들의 잠금 조건: 작업중, IK 없음, IK 준비가안됐을때, IK 이동중
        GUI.enabled = !IsBusy && ik != null & ik.Ready && !ik.IsMoving;

        GUILayout.Label("손목 각도");

        //칸에 사용할 글자 만들기 0,15, -> 0도
        string[] labels = new string[yawOptions.Length];

        // 미션
        // 반복문 for로 labels를 돌면서 labels의 인덱스에 "0도"처럼 표시하게하기
        // yawOptions.Length의 배열 크기는
        for(int i=0; i < yawOptions.Length; i++) // 0,1,2,3, 11 => 총 12개
        {
            labels[i] = $"{yawOptions[i]}도";
        }

        //SelectionGrid(지금선택된 칸 번호, 칸 글자들, 한줄에 놓을 칸수)
        //12칸을 6칸씩 2줄로 그리고, 사용자가 누른 칸 번호를 돌려준다 -> selectedYaw에 저장해서 기억하기
        selectedYaw = GUILayout.SelectionGrid(selectedYaw, labels, 6);

        //통마다 버튼 하나씩 만들기.
        for (int i = 0; i < bins.Length; i++)
        {
            if (bins[i] != null && GUILayout.Button($"{bins[i].name}으로 넣기"))
            {
                StartCoroutine(Run(i, yawOptions[selectedYaw]));
            }
        }

        //부품 리셋 버튼, 부품을 처음 위치와 회전으로 돌린다
        if (GUILayout.Button("부품 리셋 (처음 위치/회전으로)"))
        {
            ResetParts();
        }

        GUI.enabled = true; //잠금을 해제 (다른스크립트의 GUI 사용되도록)
        GUILayout.EndArea(); //영역 끝 (BeginArea 짝)
    }

    #endregion


    #region

    #endregion
}
