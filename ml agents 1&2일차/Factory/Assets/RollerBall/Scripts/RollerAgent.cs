using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;


//공(에이전트)가 무작위 위치의 큐브(목표)를 찾아 굴러간다. 
//목표에 닿으면 +1 점, 떨어지면 실패
public class RollerAgent : Agent
{
    public Transform target; //찾아가야할 큐브
    public float forceMultiplier = 10f; //값 -1 ~ 1 에 곱할 힘의 크기
    private Rigidbody rBody; //공을 굴리기 위한 물리 컴포넌트

    public override void Initialize()
    {
        rBody = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        //공이 바닥 아래로 떨어졌다면 되돌리고, 속도를 0으로 
        if (transform.localPosition.y < 0)
        {
            rBody.angularVelocity = Vector3.zero;
            rBody.linearVelocity = Vector3.zero;
            transform.localPosition = new Vector3(0, 0.5f, 0);
        }

        //목표 타겟은 매 판 무작위 배치 (-4 ~ 4) 
        //목표 타켓에 TargetMover 가 부착되어있다면 그쪽에서 위치를 지정함
        TargetMover mover = target.GetComponent<TargetMover>();
        if (mover != null)
        {
            mover.RandomStart();
        }
        else
        {
            target.localPosition = new Vector3(Random.Range(-4, 4), 0.5f, Random.Range(-4, 4));
        }            
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        //VectorSensor sensor는 신경망에 보낼 숫자를 담는 바구니

        //내위치(transform = vector3) 숫자3개
        sensor.AddObservation(target.localPosition);

        //타겟위치(transform = vector3)숫자3개
        sensor.AddObservation(transform.localPosition);

        //미션2 만약 속도 값을 관측값으로 안준다면? 
        //목표 근처에서 왔다갔다. 지나치고 돌아오고
        //속도를 모르기때문에 더 자주 떨어진다. 가장자리 근처에서 속도를 줄여야하는지 판단불가
        //학습이 쉬운 환경이기 때문에 생각보다 잘 학습 될수있음 -> 힘을 약하게 써서 천천히 움직이는 전략

        //내 속도 (float) x,z 값 숫자 2개
        sensor.AddObservation(rBody.linearVelocity.x);
        sensor.AddObservation(rBody.linearVelocity.z);

        //Space Size = 8 로 설정하기
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        //미션1 시간 벌점 추가(매 스텝마다 조금씩 점수를 깎아서 빨리 도착을 유도)
        //목적지에 가라 -> 되도록 빨리가라
        //AddReward(-0.001f); 

        //1.행동 (AI가 준 숫자 2개로 x,z방향의 힘)
        Vector3 controlSignal = Vector3.zero;
        controlSignal.x = actions.ContinuousActions[0]; // -1 ~ 1 
        controlSignal.z = actions.ContinuousActions[1];
        rBody.AddForce(controlSignal * forceMultiplier);

        //2.보상 (목표까지 가까워지면 성공, 실패)
        float distanceToTarget = Vector3.Distance(transform.localPosition, target.localPosition);

        if (distanceToTarget < 1.42f)
        {
            //거의 닿은
            SetReward(1.0f); //성공 +1
            EndEpisode();
        }
        else if (transform.localPosition.y < 0)//떨어짐
        {
            // SetReward(-1f); //떨어지면 -1 점 -> 자살방지
            EndEpisode();
        }

        //실패했는데 -1 안준 이유.
        //벌점을 안받는 것만으로도 충분히 피할수 있다. 
        //벌점 -100 크게 주게되면, 겁을 먹고 안움직임.
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var c = actionsOut.ContinuousActions;
        c[0] = Input.GetAxis("Horizontal");
        c[1] = Input.GetAxis("Vertical");
    }
}

