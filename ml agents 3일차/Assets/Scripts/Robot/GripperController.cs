using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
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

    // 판정 결과를 나중에 ML 보상에 사용
    public bool LastGraspOK { get; private set; } // 잡기 성공 여부
    public float LastAngleRight { get; private set; } // 오른쪽 손가락이 닿을 면의 기울기
    public float LastAngleLeft { get; private set; } 
    public string LastGraspInfo { get; private set; } = "_"; // 판정 결과 사람이 읽을 문장으로 저장

    //허용 각도(도) = arctan(마찰계수)를 라디안 -> 도로 변환하기 
    public float MaxAngle => Mathf.Atan(frictionCofficient) * Mathf.Rad2Deg;

    private Collider[] robotColliders; // 로봇 전체의 콜라이더 목록, 잡은 부품과의 충돌을 끌때 사용 
    readonly List<Collider> ignoredPartColliders = new List<Collider>(); // 풍돌을 꺼둔 부품 콜라이더 목록

    // 디버그 표시용 (OnDrawGizoms)
    // dbgHitA,dbgHitB 양쪽 광선이 부품 면에 처음 맞은 점 (월드 좌표)
    // dbgNormA,dbgNormB 그 점에서 부품 면의 법선(면에 수직,길이1)
    // dbgValid 그릴 값이 있는지, 판정 시작 때 false, 그리퍼 양쪽이 같은 부품에 맞으면 true
    private Vector3 dbgHitA, dbgHitB, dbgNormA, dbgNormB;
    private bool dbgValid;

    private void Start()
    {
        // transform.root 는 최상위 부모 {niryo_one} 을말함. true를 전달하여 꺼진 오브젝트 전체 콜라이더를 가져옴
        robotColliders = transform.root.GetComponentsInChildren<Collider>(true);

        // 시작할때는 손가락이 열려있는 상태로 시작
        // TODO SetTargets
        SetTargets(rightOpen,leftOpen); // 파라미터에 전달할 값 넣어주기 

    }

    // 코루틴
    public IEnumerator Close()
    {
        if(IsBusy) yield break; // 이미 동작중이면 바로 끝냄

        IsBusy = true; // 동작 중 표시를 켬
        LastGraspOK = false; // 지난번 결과 지우기

        // 1. 닫기 전에 판정 (부품이 건드려지지 않은 상태)
        // 닿은 뒤에 판정하면 부품이 이밎 밀리거나 돌아가 있어 정확하지 않음

        // 잡을 수 있는 것인지 아닌지 
        // TODO PredictGrasp

    }

    // 여기서만 사용할거니까 private
    // 그랩 판정(닫기 전)
    // out은 함수가 return 말고 값을 하나 더 돌려주는 통로 
    // 잡힐지 안잡힐지 (bool) 함수의 return 으로 돌려줌
    // 어떤 부품을 판정했는지(Rigidbody) out으로 돌려줌
    private bool PredictGrasp(out Rigidbody part)
    {
        part = null;    
        dbgValid = false; // 그릴것이 없으므로 리셋

        // 그리퍼는 손목이 돌고 팔이 기울어지니 계속 비틀어진다
        // 그래서 월드의 x,y,z 를 그대로 쓰기는 어려우므로 그리퍼 기준 방향 3가지를 만듦
        // axis = 손가락이 열고 닫히는 방향
        // up = 손가락 패드면의 세로 방향
        // side = 손가락 패드면의 가로방향
        // center = 손가락 사이의 중심

        // 그 방향을 회전 시킨 값 = 회전(Quaternion) * 방향
        // Vector3.right (1,0,0) x 축 방향
        // .transform.rotation * 을 곱해야 손가락 기준 방향을 월드 방향으로 바꿀 수 있음
        // normalized를 통해 길이를 1로 맞추고 , 방향만 남긴다 
        Vector3 axis = (rightFinger.transform.rotation * (rightFinger.anchorRotation * Vector3.right)).normalized;

        // 손가락 패드면의 세로 방향, 월드 위쪽을 axis에 수직이 되게 맞춰야함
        // 일반 Vector3.up 쓰면 안되는 이유는 로봇 팔 자세에 따라 그리퍼가 조금 기울어지면 손가락 방향도 위아래로 기울어짐
        // 그래서 ProjectOnPlane로 보정해서 항상 손가락 방향과 수직인 위쪽을 돌려주도록 함
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up,axis).normalized;

        // 손가락 패드의 가로 방향. 외적 = axis와 up 둘다에 수직인 방향
        Vector3 side = Vector3.Cross(axis,up).normalized;

        // 손가락 사이에 중심
        Vector3 center = attachPoint.position;

        // TODO FirstContact 함수 만들기 
    }



    private void SetTargets(float right, float left)
    {
        // x Drive = struct(값 복사) 꺼내서 고치고 다시 넣어줘야 적용됨
        var d = rightFinger.xDrive; // 1. 설정 묶음을 복사해옴
        d.target = right;           // 2. 복사본의 Target만 고침 (다른 값은 그대로)
        rightFinger.xDrive = d;     // 3. 고친 복사본을 관절에 다시 넣음 (이때 실제로 적용됨)

        d = leftFinger.xDrive;
        d.target =  left;
        leftFinger.xDrive = d;  
    }

}
