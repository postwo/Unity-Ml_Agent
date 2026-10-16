using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class StackTest : MonoBehaviour
{
    void Start()
    {
        //박스를 쌓을 공간(Stack)을 만듭니다.
        Stack<string> boxStorage = new Stack<string>();

        Debug.Log("=== 박스 쌓기 연습 시작! ===");

        // [미션 1] Push : 박스를 아래서부터 3개 쌓아보세요!
        // 힌트: boxStorage.Push("이름"); 
        // 1번 사과, 2번 포도, 3번 수박 순서로 쌓으세요.
        // [여기에 작성]
        boxStorage.Push("사과");
        // [여기에 작성]
        boxStorage.Push("포도");
        // [여기에 작성]
        boxStorage.Push("수박");

        // [미션 2] Peek : 꺼내지 말고 맨 위 박스 이름만 확인해보세요.
        string topbox = boxStorage.Peek();
        Debug.Log($"맨 위에는 {topbox}가 있습니다.");
         


        // [미션 3] Pop : 맨 위에 있는 박스 하나를 실제로 꺼내보세요!
        //로그 해제하기 Debug.Log($"맨 위의 {outBox}을 꺼냈습니다!");
        string outBox = boxStorage.Pop();
        Debug.Log($"맨 위의 {outBox}을 꺼냈습니다!");

        // [미션 4] Count : 이제 창고에 박스가 몇 개 남았는지 세어보세요.
        //로그 해제하기 Debug.Log($"이제 남은 박스는 총 {boxStorage.Count}개입니다.");
        Debug.Log($"이제 남은 박스는 총 {boxStorage.Count}개입니다.");
    }
}