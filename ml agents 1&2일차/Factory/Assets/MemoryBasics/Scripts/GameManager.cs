using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    // [미션 1] 'player'라는 이름의 GameObject 변수를 선언하세요.
    // 힌트: public을 붙여서 인스펙터에서 큐브를 연결할 수 있게 만듭니다.
    // [여기에 작성]
    public GameObject player;

    // [미션 2] 화면에 글자를 보여줄 'messageDisplay' 변수를 선언하세요.
    // 힌트: TMP_Text 타입을 사용하고 public으로 선언합니다.
    // [여기에 작성]
    public TMP_Text messageDisplay;


    // [미션 3] 위치 정보(Vector3)를 차곡차곡 쌓아둘 'positionHistory' 스택을 만드세요.
    // 힌트: private Stack<타입> 변수이름 = new Stack<타입>();
    // [여기에 작성]
    private Stack<Vector3> positionHistory = new Stack<Vector3>();

    void Start()
    {
        UpdateStatus("Ready");
    }

    void UpdateStatus(string message)
    {
        //주석해제 후 사용
        if (messageDisplay != null) messageDisplay.text = message;
        Debug.Log(message);

    }

    //랜덤 이동 버튼을 눌렀을 때 실행될 함수
    public void MoveRandomly()
    {
        //[미션 4] 현재 player의 위치를 나중에 되돌릴 수 있게 기록장에 먼저 넣으세요!
        // 힌트: positionHistory.Push( player의 현재 위치 );
        // [여기에 작성]
        positionHistory.Push(player.transform.position);

        // 새로운 랜덤 위치를 정합니다. (-4에서 4 사이)
        float randomX = Random.Range(-4f, 4f);
        float randomZ = Random.Range(-4f, 4f);

        // [미션 5] 큐브를 위에서 구한 랜덤 값을 사용하여 새로운 위치로 이동시키세요!
        // Y 값은 0.5f를 사용합니다. 
        // 힌트: player.transform.position = new Vector3(가로X, 높이Y, 세로Z);
        // [여기에 작성] 
        player.transform.position = new Vector3(randomX, 0.5f, randomZ);

        // [미션 6] UpdateStatus 함수를 사용하여 $"Move! (Saved: {positionHistory.Count})"를 파라미터로 전달합니다
        // [여기에 작성]

        UpdateStatus($"Move! (Saved: {positionHistory.Count})");

    }

    //되돌리기 버튼을 눌렀을 때 실행될 함수
    public void Undo()
    {
        // [미션 7] 기록장(positionHistory)에 저장된 데이터가 0개 이상인지 조건문으로 확인합니다
        // 힌트 개수 확인은 Stack의 기능인 Count 로 진행
        if (positionHistory.Count > 0)
        {
            // [미션 8] positionHistory에 데이터가 있다면 가장 최근에 저장된 위치를 하나 꺼내세요
            // 힌트 Vector3 lastPosition = 스택의 pop 사용하기
            Vector3 lastPosition = positionHistory.Pop();

             // [미션 9] 꺼내온 lastPosition을 player의 위치에 적용하세요
             // 힌트: player.transform.position = 꺼내온 위치;
             player.transform.position = lastPosition;

            // [미션 10] UpdateStatus 함수를 사용하여 $"Undo Success! ({positionHistory.Count} left"를 파라미터로 전달합니다
            UpdateStatus($"Undo Success! ({positionHistory.Count} left");
        }
        else
        {
            // [미션 11] UpdateStatus 함수를 사용하여 "Nothing to undo!"를 파라미터로 전달합니다
            UpdateStatus("Nothing to undo!");
        }

    }
}