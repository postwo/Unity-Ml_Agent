using System.Collections.Generic;
using UnityEngine;

public class QueueTest : MonoBehaviour
{
    void Start()
    {
        // [준비] 데이터를 순서대로 담을 대기열(Queue)을 만듭니다.
        Queue<string> dataQueue = new Queue<string>();

        Debug.Log("=== 실시간 데이터 수집 시작! ===");

        // [미션 1] Enqueue : 데이터를 발생한 순서대로 줄 세워보세요!
        // 힌트: dataQueue.Enqueue("데이터이름");
        // 순서: 1번 센서값, 2번 오류알람, 3번 가동중지 순서로 넣으세요.
        // [여기에 작성]
        // [여기에 작성]
        // [여기에 작성]


        // [미션 2] Peek : 줄의 맨 앞에 있는(가장 먼저 들어온) 데이터가 무엇인지 '확인'만 하세요.
        // 힌트: string firstData = ...
        // [여기에 작성]

        // Debug.Log($"가장 먼저 들어온 데이터는 {firstData}입니다."); 


        // [미션 3] Dequeue : 맨 앞의 데이터를 실제로 꺼내서 처리해봅시다! (목록에서 삭제됨)
        // 힌트: string processedData = ...
        // [여기에 작성]

        // Debug.Log($"{processedData} 처리를 완료하고 목록에서 삭제했습니다!");


        // [미션 4] Count : 현재 대기열에 남은 데이터가 몇 개인지 확인해보세요.
        // [여기에 작성]

        // Debug.Log($"현재 처리 대기 중인 데이터는 총 {dataQueue.Count}개입니다.");
    }
}