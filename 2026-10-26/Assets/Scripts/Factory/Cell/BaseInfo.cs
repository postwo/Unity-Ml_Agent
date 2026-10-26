using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

public class BaseInfo : MonoBehaviour
{
    [Tooltip("이 구멍에 맞는 부품 종류")]
    public PartType holeType = PartType.Cube;

    [Tooltip("베이스 바닥에서 구멍 바닥까지 높이 (m) = Floor 두께")]
    public float floorHeight = 0.005f;

    [Tooltip("베이스 바닥에서 윗면까지 높이 (m)")]
    public float topHeight = 0.015f;

    public Vector3 HoleCenter => transform.position;
    public float FloorY => transform.position.y + floorHeight;
    public float TopY => transform.position.y + topHeight;
    public float HoleYaw => transform.eulerAngles.y;

    public bool Accepts(PartInfo part) => part != null && part.partType == holeType;
}
