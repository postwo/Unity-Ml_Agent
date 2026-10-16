using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;


public class CleanerAgent : Agent
{
    public Transform charger; // 찾아가야 할 충전 스테이션
    public float progressScale = 0.05f; // 가까워진 1m당 보상
    public float moveSpeed = 2f;
    public float turnSpeed = 180f;


    private Rigidbody rBody;
    private float prevDistance; // 직전 스텝의 충전기까지의 거리
    const float SpawnRange = 4f;
    const float MaxDistance = 12f; // 방의 대각선 길이 (거리 정규화)

    public override void Initialize()
    {
        rBody = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        // 이전 에피소드에서 움직이던 이동속도, 회전속도를 0으로 초기화
        rBody.linearVelocity = Vector3.zero;
        rBody.angularVelocity = Vector3.zero;

        // 로봇 위치 지정
        // 장애물과 겹치지 않는 빈자리 + 무작위 방향으로 설정, 매번 다르게 배치될 수 있도록
        transform.localPosition = FindFreePosition(0.2f, 0.7f);
        transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // 충전기 배치할 무작위 빈자리 뽑기
        Vector3 p = FindFreePosition(0.25f, 0.9f);

        // 로봇과 충전기가 너무 가까우면 학습이 왜곡될 수 있음
        // 충전기 위치와 방금 배치한 로봇 위치가 3미터보다 가까우면 다시 충전기 위치를 뽑도록
        for (int i = 0; i < 20 && Vector3.Distance(p, transform.localPosition) < 3f; i++)
        {
            p = FindFreePosition(0.25f, 0.9f);
        }

        charger.localPosition = p;

        Physics.SyncTransforms(); // 옮긴 위치를 물리 엔진에 즉시 반영

        // 에피소드가 시작되는 시점에서 충전기와 로봇의 초기 거리를 측정해서 변수에 저장
        prevDistance = DistanceToCharger();
    }


    // 관측 (총 4개 → Behavior Parameters의 Space Size = 4)
    public override void CollectObservations(VectorSensor sensor)
    {
        // 충전기가 어느 쪽에, 얼마나 멀리 있는지, 나는 지금 얼마나 빨리 가고 있는지
        // 충전기가 로봇 기준 오른쪽, 왼쪽, 앞, 뒤

        // 로봇 눈에 비친 충전기 위치 구하기
        // 월드 좌표 기준으로 '충전기 위치 - 로봇 위치'를 빼서 거리와 방향을 가진 벡터가 만들어짐
        // InverseTransformDirection 함수를 사용해 로봇이 바라보는 시점(로컬 기준)으로 변환
        Vector3 local = transform.InverseTransformDirection(charger.position - transform.position);

        // 바닥을 돌아다니는 로봇이니 y는 무시
        local.y = 0f;

        // 벡터에서 거리는 제외하고 방향만 사용하도록 normalized 함
        Vector3 dir = local.normalized;

        // 충전기가 로봇을 기준으로 어느 방향에 있는지를 전달
        sensor.AddObservation(dir.x); // 오른쪽(+) 왼쪽(-)
        sensor.AddObservation(dir.z); // 앞(+) 뒤(-)

        // 충전기까지의 실제 거리(local.magnitude)를 구한 뒤
        // 방의 대각선 최대 거리인 12로 나눠서 0과 1 사이로 압축(정규화)함
        // AI는 너무 큰 숫자보다 0~1 사이로 정규화됐을 때 더 잘 학습함
        sensor.AddObservation(Mathf.Clamp01(local.magnitude / MaxDistance));

        // 로봇이 정면 방향으로 얼마나 빨리 움직이고 있는지
        // 현재 이동속도를 최대속도(moveSpeed)로 나누어, 전진 중이면 + 후진 중이면 - 가 되는 -1 ~ 1 사이의 속도값을 전달
        sensor.AddObservation(Vector3.Dot(rBody.linearVelocity, transform.forward) / moveSpeed);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 행동
        // 신경망이 고른 연속 행동 2개가 있음 (왼쪽 바퀴, 오른쪽 바퀴)
        // 두 값이 같으면 전진, 다르면 회전
        // Clamp를 사용하여 범위를 벗어난 값이 왔을 때 -1~1로 안전하게 자르기
        // Clamp를 사용한 이유는 Heuristic 키보드 조작 때문
        float left = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float right = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        Move(left, right);


        // 보상
        // 시간 벌점
        // MaxStep으로 나눠서 매 스텝마다 벌점을 주되, 끝까지 헤맸을 때는 총량이 -1
        if (MaxStep > 0)
        {
            AddReward(-1f / MaxStep);
        }

        // 거리 보상
        // 어느 방향으로 가야 하는지 알려줌
        // 도착 보상만 있으면 우연히 충전기에 닿을 때까지 계속 헤매며 학습 -> 느리다
        // 거리 보상은 매 스텝마다 "방금 그 방향 좋아"
        // 다가갈 때 +만 준다면 -> 충전기 앞에서 왔다 갔다 하면서 점수를 쌓으려는 꼼수 발생
        // 대칭으로 주면 왕복 합계가 0 -> 꼼수 불가. 진짜 도착해야 보상.

        float d = DistanceToCharger();

        // 이전 거리 - 지금 거리
        AddReward((prevDistance - d) * progressScale); // progressScale: 거리 보상의 세기를 조절하는 배율
        prevDistance = d;
    }

    // 도착 보상
    // 충전기와 닿았을 때 목표 달성이므로 보상 +1 지급
    // (충전기에 "Target" 태그 + Collider의 Is Trigger 체크 필요)
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Target"))
        {
            AddReward(1f);
            EndEpisode();
        }
    }

    // 충돌 벌점
    // 벽, 장애물
    // 충돌 함수 만들기 (Trigger, Collision), 태그 비교 방식으로 if 문 조건
    // 보상 -1
    // 에피소드 종료
    private void OnCollisionEnter(Collision collision)
    {
        // 부딪힌 물체의 태그가 "Wall"일 때는 벌점을 줌
        if (collision.collider.CompareTag("Wall"))
        {
            AddReward(-1f);
            EndEpisode();
        }
    }

    // 키보드 조작 (Active Input Handling이 Both 또는 Old여야 Input.GetAxis 사용 가능)
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> a = actionsOut.ContinuousActions;

        float forward = Input.GetAxis("Vertical");
        float turn = Input.GetAxis("Horizontal");

        a[0] = Mathf.Clamp(forward + turn, -1f, 1f);
        a[1] = Mathf.Clamp(forward - turn, -1f, 1f);
    }

    private void Move(float left, float right)
    {
        float forward = (left + right) * 0.5f; // 두 바퀴의 평균 = 앞으로 가는 정도
        float turn = (left - right) * 0.5f;    // 두 바퀴의 차이 = 회전의 정도

        // 전진 속도 적용
        Vector3 v = transform.forward * forward * moveSpeed;
        v.y = rBody.linearVelocity.y; // 원래 y 속도(중력) 그대로 유지
        rBody.linearVelocity = v;

        // 회전 속도 적용
        // turnSpeed는 180(도/초)인데 angularVelocity는 라디안/초 단위이다. 그래서 변환을 위해 Mathf.Deg2Rad 사용
        rBody.angularVelocity = new Vector3(0f, turn * turnSpeed * Mathf.Deg2Rad, 0f);
    }


    // 충전기까지의 수평 거리
    // y = 0으로 높이 차이를 지우고, 바닥 평면 기준 거리만 구했다
    // Vector는 방향과 크기(길이)
    // magnitude는 Vector3의 길이만 사용하겠다는 뜻
    private float DistanceToCharger()
    {
        Vector3 diff = charger.localPosition - transform.localPosition;
        diff.y = 0f;
        return diff.magnitude;
    }

    // 장애물과 겹치지 않는 무작위 빈 좌표를 찾아 반환한다
    // 높이 값(y)과 주변에 장애물이 없어야 하는 안전 반경을 입력받아서
    private Vector3 FindFreePosition(float y, float clearRadius)
    {
        // 최대 50번까지 시도 (겹치는 자리면 다시 뽑음)
        for (int i = 0; i < 50; i++)
        {
            // 허용된 범위(-4~4) 안에서 x축과 z축을 무작위로 뽑는다
            // 매개변수로 전달받은 y값(높이)을 조합해서 로컬 좌표계 기준의 임시 위치를 만들었음
            Vector3 local = new Vector3(Random.Range(-SpawnRange, SpawnRange), y, Random.Range(-SpawnRange, SpawnRange));

            // 주변에 벽이 있는지 검사할 때 Physics.OverlapSphere(중심점, 반지름)
            // 이 함수는 유니티 전체 공간(world space)을 기준으로 작동. 그래서 월드 값 필요
            Vector3 world = transform.parent.TransformPoint(new Vector3(local.x, 0.5f, local.z));

            bool blocked = false;

            // 월드 좌표를 중심으로 clearRadius 크기의 가상의 구를 만들어 겹쳐본다
            // 그 안에 닿아 있는 물체를 모두 가져오기
            foreach (var col in Physics.OverlapSphere(world, clearRadius))
            {
                // 가져온 물체 중에 Tag가 "Wall"인 것이 하나라도 있다면
                // 그 자리는 장애물이 있는 자리임. blocked 변수를 true로 변경, 검사를 중단한다
                if (col.CompareTag("Wall"))
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                return local;
            }
        }

        return new Vector3(0f, y, 0f); // 50번 실패하면 가운데로 설정
    }
}