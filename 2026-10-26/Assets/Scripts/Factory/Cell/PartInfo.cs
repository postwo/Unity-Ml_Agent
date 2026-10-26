using UnityEngine;

public enum PartType { Cube, Prism }

public class PartInfo : MonoBehaviour
{
    public PartType partType = PartType.Cube;

    [Tooltip("부품 중심에서 바닥까지 높이 (m). 큐브 0.015, 삼각기둥 0.013")]
    public float halfHeight = 0.015f;

    [Tooltip("몇 도마다 같은 모양인지. 큐브 90, 삼각기둥 180")]
    public float symmetryDeg = 90f;

    [Tooltip("잡는 위치: 부품 중심에서 위로 몇 m (m). 큐브 0.013, 삼각기둥 0.010\n" +
             "높게 잡아야 손가락 끝이 베이스에 닿지 않고 부품 바닥을 구멍에 넣을 수 있음")]
    public float gripOffset = 0.013f;
}
