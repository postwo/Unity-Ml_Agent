using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

//유니티 함수
//Awake, Start -> Initialize()
//리셋 로직 -> OnEpisodeBegin()
//Update 입력부 -> CollectObservations()(무엇을 봐야할지, 관측할지)
//Update 행동부 -> OnActionReceived() (무엇을 할지) 
//Input 처리 -> Heursitic (사람이 조작)

public class TemplateAgent : Agent
{
    [SerializeField]
    private Transform target;
    [SerializeField]
    private float moveSpeed = 5f;

    private Rigidbody rb;
    private Vector3 startPos;

    //Awake, Start의 대용
    //Agent 가 실행될때 딱 1번만 호출
    //용도: Getcomponent나 초기위치 저장, 캐싱 작업
    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        startPos = transform.localPosition;
    }

    // OnEpisodeBegin 
    // 매 에피소드마다 시작시 호출, 환경을 리셋함
    // 에이전트, 타겟의 위치를 랜덤화 하거나, 속도 초기화, 내부 변수 초기화
    // 호출 시점: 첫 에피소드, EndEpisode 호출 후에 MaxStep 도달 후
    public override void OnEpisodeBegin()
    {
        transform.localPosition = startPos;

        //rb의 물체 움직임을 멈춘다
        rb.linearVelocity = Vector3.zero; //이동속도 (0,0,0)
        rb.angularVelocity = Vector3.zero; //회전속도 (0,0,0)

        target.localPosition = new Vector3(Random.Range(-4, 4), 0.5f, Random.Range(-4, 4));
    }

    //CollectObservations
    //에이전트의 눈 역할. 관측
    //정보값을 기반으로 상황 파악
    //AddObservation 한 값의 총개수 (float = 1, Vector2 = 2,Vector3 = 3, Quaterinion = 4)
    //값을 대략 -1 ~ 1 범위로 정규화하면 학습잘됨
    public override void CollectObservations(VectorSensor sensor)
    {
        //에이전트 자신의 위치
        sensor.AddObservation(transform.localPosition / 5f); //3개

        //목표물의 위치
        sensor.AddObservation(target.localPosition / 5f); //3개

        //에이전트의 현재 이동 속도 좌우(x) , 앞뒤(z)
        sensor.AddObservation(rb.linearVelocity.x / moveSpeed); //1개
        sensor.AddObservation(rb.linearVelocity.z / moveSpeed); //1개

        //Space size = 8 
    }

    //OnActionReceived 
    //에이전트가 고른 행동을 받아서 실행
    //Update 에 행동 로직을 뒀던것처럼 동일하게 사용
    //용도 : 이동/회전, 보상 부여, 종료 조건 체크
    //ActionBuffers는 신경망이 내린 결정을 넘겨줌. 
    //두종류 Continuous(연속행동) -1 ~ 1
    //Discrete(이산 행동) 0,1,2 정수값이 들어옴 어느것을 고를지 결정
    //0 = 정지, 1 = 점프, 2 = 공격
    public override void OnActionReceived(ActionBuffers actions)
    {
        //행동 
        //신경망이 정한 첫번째 값 좌우 이동방향과 세기를 꺼낸다(-1 ~ 1)
        float moveX = actions.ContinuousActions[0];
        //두번째 값 앞뒤 이동방향과 세기를 꺼낸다(-1 ~ 1)
        float moveZ = actions.ContinuousActions[1];
        rb.AddForce(new Vector3(moveX, 0f, moveZ) * moveSpeed);

        //매 행동마다 보상과 벌점주기
        //에이전트는 maxStep
        //시간패널티: 빨리 끝내도록 유도를 함

        //AddReward 이번 스텝 보상에 값을 더한다
        //사용예시: 조금씩 쌓이는 보상, 시간 벌점, 목표에 가까워질때 주는 작은보상

        //SetReward 이번 스텝 보상을 그 값으로 덮어쓴다
        //결과를 확실하게 정할때 성공, 실패 처럼 에피소드를 끝내는 순간

        AddReward(-1f / MaxStep); //-0.001

        float dist = Vector3.Distance(transform.localPosition, target.localPosition);
        if (dist < 1.2f)
        {
            //성공
            SetReward(1f); //덮어씀. -0.001 은 사라짐 // 현재 step 이번 보상은 +1점
            EndEpisode();
        }
        else if (transform.localPosition.y < -1f)
        {
            //실패 (떨어짐)
            SetReward(-1f);
            EndEpisode();
        }
    }

    //Heuristic
    //사람이 직접 조작하는 "수동 모드" 학습하기전 테스트때 사용
    //Behavior type = Heuristic only 이거나 Default라도 연결된 모델이 없을때
    //행동 로직이 제대로 되는지 먼저 검증
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = Input.GetAxis("Horizontal");
        continuous[1] = Input.GetAxis("Vertical");
    }

    //OnCollisionEnter TriggerEnter같은 충돌함수도
    //물리충돌 학습에 자주 사용. 충돌에 의해 보상하거나 종료 처리하는것이 흔한 패턴
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Wall"))
        {
            AddReward(-0.5f);
            EndEpisode();
        }
    }
}
