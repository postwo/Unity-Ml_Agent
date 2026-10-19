
using System.Xml.Serialization;
using UnityEngine;



// 손가락 하나가 "지금 부품에 닿아있는지" 알려주는 역할
// FingerContact "닿았다" 만 알려주고, "잡았다" -> GripperController에서 함
// GripperController에서 손가락 두개가 같은 부품을 잡았는지, 한쪽만 닿은 것은 잡은것이 아니다라고 판단.

public class FingerContact : MonoBehaviour
{
    public float holdTime = 0.1f; // 마지막 접촉 후 이 시간까지는 "닿아있음"으로 인정"
    private Rigidbody touching; // 마지막으로 닿은 부품
    private float lastTouchTimne = 1f; // 마지막으로 닿은 물리 시간 , -1 이면 한번도 안닿았다 

    // 최근 holdTime 안에 닿았던 부품을 돌려주는 
    public Rigidbody TouchingPart
    {
        get
        {
            // 닿은 부품이 없거나, 마지막 접촉이 holdTime 보다 오래전이다 -> 안닿은 것
            if (touching == null || Time.fixedTime - lastTouchTimne > holdTime)
            {
                // null 리턴
                return null;
            }
            return touching;
        }
    }

    // 충돌 함수
    // 충돌 함수 처음 닿는 순간 한 번 Enter, 닿아있는동안 매번 Stay

    private void OnCollisionEnter(Collision collision) => Record(collision);
    private void OnCollisionStay(Collision collision) => Record(collision);

    // 닿은 것이 "부품" 이면 무엇이, 언제 닿았는지를 기록
    private void Record(Collision collision)
    {
        //부품 본체를 가져오기
        // 부품은 자식 메시 콜라이가 있을수 있으니, 본체인 Rigidbody를 기준으로 기록
        Rigidbody rb = collision.collider.attachedRigidbody;

        // 본체가 존재해야하고, 태그가 part인지 체크
        if(rb != null && rb.CompareTag("Part"))
        {
            // 부품 변수에 rb를 넣기 
            touching = rb;
            // 언제 닿았는지 변수에는 Time.fixedTime;
            lastTouchTimne = Time.fixedTime; // fixedTime (충돌함수가 물리 스텝 FixedUpdate에서 불린다. 같은 물리시간을 사용하면 좋음 )
        }
    }

    public void Clear()
    {
        // 기록 초기화 
        touching = null;
        lastTouchTimne = -1f;
    }


}
