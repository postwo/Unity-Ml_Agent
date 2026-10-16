using UnityEngine;
using TMPro;

public struct ItemStruct { public int level; }
public class ItemClass { public int level; }

public class InventoryManager : MonoBehaviour
{
    public TMP_Text statusDisplay;

    ItemStruct myStructItem = new ItemStruct { level = 1 };
    ItemClass myClassItem = new ItemClass { level = 1 };

    void Start()
    { 
        UpdateUI();
    }

    public void OnClickUpgradeStruct()
    {
        UpgradeStruct(myStructItem);
        UpdateUI();
    }

    public void OnClickUpgradeClass()
    {
        UpgradeClass(myClassItem);
        UpdateUI();
    }

    void UpgradeStruct(ItemStruct item)
    {
        item.level++;  
        Debug.Log("Struct 함수 내부 레벨: " + item.level);
    }

    void UpgradeClass(ItemClass item)
    {
        item.level++;  
        Debug.Log("Class 함수 내부 레벨: " + item.level);
    }

    void UpdateUI()
    {
        statusDisplay.text = $"Struct Level: {myStructItem.level}\n" +
                             $"Class Level: {myClassItem.level}";
    }
}