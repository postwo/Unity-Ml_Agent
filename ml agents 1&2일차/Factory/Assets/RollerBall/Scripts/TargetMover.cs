using UnityEngine;

// 타겟을 어떻게 움직일까
// 움직임이 너무 복잡하면 -> 길찾기 코드처럼 나잉도가 올라감
// 무작위로 꺾여서 돌아다님 -> 학습에 방해
// 원 운동을 사용해서 맴밖으로 타겟이 나가지 않으면서도 공간안에서 자유롭게 돌아다니도록 

public class TargetMover : MonoBehaviour
{
    // 1라디안 = 몇도
    // 180도 = 파이
    // 원 한바퀴는 360, 2파이 (6.28 라디안)

    public float speed = 0.5f; // 6.28 / 0.5ch -> 12.3초 에 한바퀴를 돔

    public float minRadius = 1.5f;
    public float maxRadius = 4f;

    // 수학에서 원위에 현위치를 지정할때는 원의 크기, 각도
    // 원 크기를 -> 중심에서 얼마나 떨어져있는지 정의
    // 원 각도 -> 원위에서 어느 방향에 있는지를 정의 

    private float radius; // 현재 반지름 (한 판마다 새로 뽑는다)
    private float angle; // 현재각도 (매 물리 스탭마다 조금씩 커짐)

    // 시작 위치 지정 함수
    public void RandomStart()
    {
        // 완벽한 한 바퀴를 만들기위해서 Mathf.pi * 2f 해서 360도
        angle = Random.Range(0,Mathf.PI * 2f);
        radius = Random.Range(minRadius, maxRadius);
    }

    // update 화면이 그려질 때마다 호출, 컴퓨터 성능에 따라서 간격이 들쭉날쭉
    // FixedUpdate 물리 계산 주기마다 호출. 기본 0.02초(초당 50번) 으로 일정
    // 학습중에는 Unity가 시간을 빠르게 돌림 일정한 FIxed update를 사용해야
    // 공 Rigidbody가 같은 시간 기준으로 움직인다 
    private void FixedUpdate()
    {
        // 이번 스텝에 돌 각도 = 속도 * 시간
        // 0.5 *0.02 = 0.01(0.57도) 씩 증가
        angle += speed * Time.fixedDeltaTime;
        UpdatePosition();
    }


    // 현재 각도와 반지름으로 원의 위치를 계산해서 큐브를 옮기는 
    // transform.lovalPosition으로 작성한 이유 
    // TraingArea의 자식으로 Targetmover가 존재하기 때문에, 월드 좌표(position)을 쓰면 큐브가 씬의 원점을 기준으로 돌아감 

    private void UpdatePosition()
    {
        // 수학적으로 원을 그리는 공식은 
        // x 좌표 = 반지름 * 코사인
        // z 좌표 = 반지름 * 사인
        // y 값 = 큐브가 바닥에 올라오도록 0.5
        transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, 0.5f, Mathf.Sin(angle) * radius);
    }
}
