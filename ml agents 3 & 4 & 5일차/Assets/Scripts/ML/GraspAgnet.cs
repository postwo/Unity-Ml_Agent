using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using UnityEngine;

//GraspAgent는 이미지만 보고 손목각도를 고른다.
//에피소드 1회 = 부품 1개
//로봇 대기 위치로 -> 부품을 랜덤위치, 랜덤회전으로 생성
//판단요청 -> 손목 각도를 선택
//그 각도로 내려가서 잡고 들어올림
//잡혀있으면 +1, 놓치면 failReward -> 다음 에피소드

public class GraspAgent : Agent
{
    #region 1.에피소드 시작 (부품 생성, 카메라 이동, 판단 요청)

    [Header("연결")]
    public PickPlaceSequence sequence; //실제로 잡는 일을 맡김
    public ArmIK ik;
    public GripperController gripper;

    [Header("부품 프리팹 (순서와 대칭 각도를 맞추세요)")]
    public Rigidbody[] partPrefabs; //프리팹 cubepart, prismpart
    public float[] symmetryDeg = { 90f, 180f }; //각 프리팹이 몇도마다 같은 모양인지. 큐브 90 삼각기둥 180

    [Header("비전 카메라")]
    public Transform visionCamera;
    public bool centerCameraOnPart = true;

    [Header("생성 범위 (월드 좌표,m)")]
    public Vector2 spawnRangeX = new Vector2(0.23f, 0.33f); //x최소 y최대
    public Vector2 spawnRangeZ = new Vector2(-0.06f, 0.06f);
    public float settleTime = 0.5f; //생성후 안착 대기

    //지금 에피소드 상태
    private Rigidbody currentPart; //이번 에피소드에서 생성한 부품
    private int currentPrefabIndex; //그 부품이 partPrefabs의 몇번째인지
    private bool waitingForDecision; //판단을 기다리는 중인지 (판단이 두번들어오는 것을 방지)

    //부품별 통계 (큐브, 프리즘)
    private int[] partTryCount;     //부품별 시도수
    private int[] partSuccessCount; //부품별 성공 수
    private int[] partRecentSuccessCount; //부품별 최근 성공수
    private Queue<int>[] partRecentResults; //부품별 최근 결과
    private const int PartRecentWindow = 50; //부품별 최근 50회

    //Initialize 는 Agent를 처음 만들때 한번만 호출(Awake 랑 비슷)
    public override void Initialize()
    {
        if (sequence == null)
        {
            Debug.LogError("[GraspAgent] sequence가 비어있습니다.");
        }
        if (partPrefabs == null || partPrefabs.Length == 0)
        {
            Debug.LogError("[GraspAgent] part Prefabs가 비어있습니다.");
        }
        if (symmetryDeg.Length != partPrefabs.Length)
        {
            Debug.LogWarning("[GraspAgent] symmetry Deg 개수를 part Prefabs 개수와 맞추세요. ");
        }

        int partCount = partPrefabs.Length; //부품 개수
        partTryCount = new int[partCount];
        partSuccessCount = new int[partCount];
        partRecentSuccessCount = new int[partCount];
        partRecentResults = new Queue<int>[partCount]; //Queue를 담을 칸만듦


        //숫자배열은 만들면 바로 0으로 채워지지만 Queue 같은 객체 배열은 null로 채워짐.
        //그래서 칸마다 new로 하나씩 만들어 넣어줘야함
        for (int i = 0; i < partCount; i++)
        {
            //칸마다 진짜 Queue를 하나씩 만들어 넣기
            partRecentResults[i] = new Queue<int>();
        }
    }

    //Agent가 에피소를 시작할때 자동 호출
    public override void OnEpisodeBegin()
    {
        StopAllCoroutines(); //남아있는 코루틴을 멈추고 새 에피소드 시작 준비
        StartCoroutine(BeginEpisode());
    }

    //로봇이동, 안착 대기처럼 시간이 걸리는 일이있어서 코루틴으로 만듦
    //이전 부품 정리 -> 로봇 대기 위치로 -> 새 부품 생성 -> 카메라를 부품 위로 -> 판단 요청
    IEnumerator BeginEpisode()
    {
        waitingForDecision = false;

        //1.이전 부품 정리
        if (gripper.HeldPart != null)
        {
            yield return gripper.Open();
        }

        if (currentPart != null)
        {
            currentPart.gameObject.SetActive(false); // 카메라에 남지 않도록 즉시 숨김
            //Destroy는 프레임 끝에 지워짐
            Destroy(currentPart.gameObject);
            currentPart = null;
        }

        //2. 로봇이 한가할때까지 기다린뒤, 대기 위치로 보낸다
        // 미션1
        // 기다리기를시전, 어떤조건일대 기다려야하는지 생각해보고 구현
        yield return new WaitUntil(() => ik.Ready && !ik.IsMoving && !sequence.IsBusy && !gripper.IsBusy);

        //미션2
        //ik 사용해서 대기위치로 이동
        yield return ik.MoveTo(sequence.homePosition, 0f);

        //3.부품 생성: 랜덤 종류, 랜덤 위치, 랜덤 회전(0~ 180도)
        currentPrefabIndex = Random.Range(0, partPrefabs.Length); //Range(0,n) -> 0 ~ n-1 개 뽑음
        Rigidbody prefab = partPrefabs[currentPrefabIndex];
        Vector3 spawnPosition = new Vector3(
            Random.Range(spawnRangeX.x, spawnRangeX.y),
            prefab.transform.position.y,
            Random.Range(spawnRangeZ.x, spawnRangeZ.y)
            );

        Quaternion spawnRotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);

        // 부품생성을 위해서는 어떤것을 생성할지와 생성 위치, 회전 값

        // 미션1
        // Instantiate 함수를 사용하여 부품을 생성하세요. 생성한 부품을 저장합니다 
        currentPart = Instantiate(prefab, spawnPosition, spawnRotation);

        // 4. 멀어지거나 흔들리던 부품이 멈출때까지 대기 
        yield return new WaitForSeconds(settleTime);

        //5. 카메라를 부품 바로 위로 이동 (x, z 값을 부품과 같게. 카메라의 높이 회전은 그대로)
        // 부품이 매번 다른위치에 생기므로, 카메라를 옮겨야 항상 사진 가운데 부품이 놓인다.
        if (centerCameraOnPart && visionCamera != null && currentPart != null)
        {
            Vector3 cameraPosition = visionCamera.position;
            Vector3 partPosition = currentPart.position;

            visionCamera.position = new Vector3(partPosition.x, cameraPosition.y, partPosition.z);
        }

        //6. 판단 요청
        waitingForDecision = true;
        RequestDecision();

        //RequestDecision() 는 Agent클래스안에 있는 함수. "지금 판단해줘"라고 요청
        //Decision Requester(컴포넌트)는 정해진 간격마다 자동으로 요청
        //Decision Requester 를 사용하지 않는 이유: 부품이 아직 떨어지는 중이거나 카메라가 옮겨지기전에 판단되면 안되서.
    }

    //관측 정보 CollectObservation()은 작성하지 않음.
    //Camera Sensor 가 자동으로 관측 정보를 넘겨주기 때문.

    #endregion

    #region 2.행동과 보상 (고른 각도로 잡고 점수 주기)

    [Header("행동")]
    public float yawStep = 15f; //번호 * 15도 = 손목 각도 (0~165도, 12 개)

    [Header("보상")]
    public float successReward = 1f;
    public float failReward = -0.1f;

    [Header("모드")]
    public bool trainingMode = true; //켜면 학습모드:  잡고 들어 올려서 성공여부만 확인 (통으로 옮기지 않는다)
                                     //꺼져있으면 시연모드 : 잡은 부품을 통에 넣는다

    public int[] binForPrefab = { 0, 1 }; //큐브 -> 0번 통 , 삼각기둥은 -> 1번통
    public int maxPartsKeptInBins = 10; //시연모드에서 통에 남겨둘 부품 최대 개수
    private readonly Queue<GameObject> placedParts = new Queue<GameObject>();  //시연 모드에서 통에 넣은 부품들


    //신경망이 행동을 고르면 ml agent OnActionReceived를 호출
    //행동 번호(0~11) 를 손목 각도로 바꿔서 실행한다.
    public override void OnActionReceived(ActionBuffers actions)
    {
        //판단을 요청한 상태가 아니라면 무시 (한 에피소드에 한번만 행동)
        if (!waitingForDecision || currentPart == null) { return; }

        waitingForDecision = false;

        // DiscreteActions[0] 은 0번 가지에서 고른 번호 (0~11)
        // 손목각도 = 신경망이 골라준 번호 * yawStep(15도)
        float wristYaw = actions.DiscreteActions[0] * yawStep;
        StartCoroutine(Act(wristYaw));
    }

    // 고른 각도로 실제로 잡아보고, 결과를 보고 점수를 준뒤에 에피소드를 끝냄
    IEnumerator Act(float wristYaw)
    {
        // 시연 모드에서 넣을 통 번호(칸이 없으면 0번 통)
        int binIndex = currentPrefabIndex < binForPrefab.Length ? binForPrefab[currentPrefabIndex] : 0;

        // 잡기 실행
        yield return sequence.Run(binIndex ,wristYaw ,trainingMode ,currentPart);

        // 결과 : 들어올린 뒤에도 잡혀있으면 성공
        bool success = sequence.LastSuccess;
        // TODO Recod 함수 만들고 호출하기

        // 보상
        SetReward(success ? successReward : failReward);

        // 시연 모드에서 통에 넣은 부품은 남겨둬야 한다 (다음 에피소드에서도 삭제하지 않음)
        if (!trainingMode && success && currentPart != null)
        {
            placedParts.Enqueue(currentPart.gameObject);
            currentPart.tag = "Untagged";
            currentPart = null; //null 처리해서 비워야 BeginEpisode에서 삭제되지 않음.

            if (placedParts.Count > maxPartsKeptInBins)
            {
                GameObject oldestPart = placedParts.Dequeue(); // 가장 오래된 부품

                if (oldestPart != null)
                {
                    Destroy(oldestPart);    //가장 먼저 넣은 부품부터 지운다
                }
            }
        }

        EndEpisode(); // Agent가 onEpisodeBegin 다시 호출 -> 다음 부품 = 반복
    }

    #endregion
}