using UnityEngine;


// 1. struct: 값 형식 (데이터가 작고 독립적일 때)
public struct PlayerPosStruct
{
    public int x;
}

// 2. class: 참조 형식 (데이터가 크고 공유가 필요할 때)
public class PlayerPosClass
{
    public int x;

}

public class MemoryTest : MonoBehaviour
{
    void Start()
    { 
        PlayerPosStruct structA = new PlayerPosStruct();
        structA.x = 10;

        PlayerPosStruct structB = structA;  
        structB.x = 20;  

        Debug.Log($"Struct A: {structA.x}, Struct B: {structB.x}");



        PlayerPosClass classA = new PlayerPosClass();
        classA.x = 10;

        PlayerPosClass classB = classA; 
        classB.x = 20;  

        Debug.Log($"Class A: {classA.x}, Class B: {classB.x}");
    }
}