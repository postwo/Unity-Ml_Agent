using UnityEngine;

// 그리퍼를 XDrive로 실제로 열고 닫는다
// Grasp 판정을 통과한 경우에만 양쪽 손가락이 닿는 순간 부품을 붙인다
// 판정 근거 : 마찰 원뿔, 손가락이 닫히는 방향과 손가락이 처음 닿을 부품면의 법선 사이 각도가
// 마찰 원뿔 반각이내여야 미끄러지지않고 잡힌다 

// 판정 시점: 닫기 시작하기 직전 (부품이 손가락에 밀리거나 돌아가기전 상태로 판정)
// 판정 방법: 손가락 패드 면적에 해당하는 3*3 광선 격차를 양쪽에서 쏴서 
// 각 쪽에서 먼저 맞는 면의 각도를 잰다 .

// 그리퍼가 Close -> 닫기 전 판정 -> 손가락을 조금씩 닫음 -> 양쪽 손가락에 같은 부품이 닿았다면 (판정 통과 - 그자리에서 멈추고 붙인다(attach)/ 판정 실패 - 살짝 벌려서 미끄러지게함)

// 그리퍼가 Open -> 부품을 떼고(Detach) 손가락을 엶 -> 잠시 뒤 로봇-부품 충돌을 다시 켬

public class GripperController : MonoBehaviour
{
    [Header("손가락 AtriculationBody")]
    public ArticulationBody rightFinger;
    public ArticulationBody leftFinger;
    public FingerContact rightContact; // 각 손가락에 붙은 FingerContact 가 "지금 어떤 부품에 닿아있는지" 알려줌
    public FingerContact leftContact;

    [Header("열림/닫힘 값")]
    // 각 손가락 xDrive의 Tartget에 넣는 값
    // 두 손가락이 서로 마주 보고있으므로 부호가 반대
    // 오른쪽 -0.005 열림, +0.005 닫힘
    // 변수 4개 왼쪽 오른쪽에 open, close 변수 만들고 초기값 넣기
    public float rightOpen = -0.005f;
    public float rightClose = 0.005f;
    public float leftOpen = 0.005f;
    public float leftClose = -0.005f;
    public float moveTime = 0.5f; // 열림 -> 닫힘까지 걸리는 시간 (초)
    public float extraColseTime = 0.5f; // 목표 닫힘에 도달한 뒤에도 손가락이 늦게 닿는것을 대비하여 기다리는 시간

    // 변수
    // 부품을 붙일 위치
    [Header("부품을 붙일 위치(손가락 사이 정중앙)")]
    public Transform attachPoint;

    // Grasp 파지 판정
    [Header("파지 판정")]
    public bool useGraspCheck = true; //false면 양쪽이 닿기만하면 무조건 잡힘
    public float frictionCofficient = 0.36f;//  약 19.8도, 면이 이각도보다 기울어져있다면 미끄러진다 
    public float padHalfHeight = 0.006f; // 패드 = 손가락 안쪽에서 부품에 닿는 면 
    public float padHalfWidth = 0.006f; // 손가락 패드 절반 폭
    public float slipBackOff = 0.001f; // 판정 실패시 손가락을 살짝 벌려 미끄러진 상태로 만드는 거리 
    
    public float restoreCollisionDelay = 0.3f; // 놓자마자 충돌을 켜면 손가락과 겹친 부품이 튕겨나감. 부품이 빠질시간 주기

    [Header("테스트")]
    public bool showTestButtons = true; // 화면에 버튼 표시 여부


    public Rigidbody HeldPart { get; private set; } // 지금 잡고있는 부품
    public bool IsBusy { get; private set; } // 열고 닫는 중이면 true, 동작중 중복 실행을 막음


    
}
