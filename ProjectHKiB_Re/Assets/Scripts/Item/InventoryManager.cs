using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using UnityEngine;
public class InventoryManager : MonoBehaviour
{
    public SerializedDictionary<int, Item> playerInventory = new();
    public SerializedDictionary<int, Gear> playerGearInventory = new();

    public Action OnInventoryChanged;
    public Action OnGearInventoryChanged;
    void Start() // temp
    {
        ItemDataSO[] items = Resources.LoadAll<ItemDataSO>("Items");
        foreach (ItemDataSO data in items)
        {
            //Debug.Log(data.name);
            AddItem(data, 99);
        }
        GearDataSO[] gears = Resources.LoadAll<GearDataSO>("Items/Gears");
        /*
        foreach (GearDataSO data in gears)
        {
            //Debug.Log(data.name);
            AddItem(data, 99);
            AddGear(data);
        }*/
    }

    public void AddItem(ItemDataSO item, int count)
    {
        if (!item) return;
        int ID = item.GetInstanceID();
        if (playerInventory.ContainsKey(ID))
            playerInventory[ID].StackItem(count);
        else
            playerInventory[ID] = new(item, count);
        OnInventoryChanged?.Invoke();
    }
    public void AddGear(GearDataSO data)
    {
        playerGearInventory[data.GetInstanceID()] = new(data);
        OnGearInventoryChanged?.Invoke();
    }

    public Item GetItem(int ID)
    {
        if (!playerInventory.ContainsKey(ID)) return null;
        return playerInventory[ID];
    }

    public Item GetItemByIndex(int index)
    {
        Item[] items = playerInventory.Values.ToArray();
        if (items.Length > index)
            return items[index];
        else return null; // or defaultItem
    }

    public bool UseInventoryItem(int ID, int count)
    {
        if (!playerInventory.ContainsKey(ID) || playerInventory[ID].ItemCountCheck(count))
            return false;
        playerInventory[ID].UnstackItem(count);
        //Initialize(playerInventory[ID].ItemEvent); // play event
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void RemoveInventoryItem(int ID, int count)
    {
        if (!playerInventory.ContainsKey(ID)) return;
        playerInventory[ID].UnstackItem(count);
        OnInventoryChanged?.Invoke();
    }
}