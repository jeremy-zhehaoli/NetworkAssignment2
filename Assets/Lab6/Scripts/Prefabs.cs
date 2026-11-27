using UnityEngine;

[CreateAssetMenu(fileName = "Prefabs", menuName = "Scriptable Objects/Prefabs")]
public class Prefabs : ScriptableObject
{
    public static Prefabs Instance { get; private set; }

    public enum PrefabId: byte  { Player = 1, Bullet = 2 }
    //Maybe we should change it to use a GameObjects list, but for now, since there arent a bunch of object
    //types, this way's easier and we make sure that the id corresponds with the desired prefab
    [SerializeField] GameObject playerPrefab, bulletPrefab;
    private void OnEnable()
    {
        Instance = this;
    }
    public GameObject GetPrefab(byte id) 
    {
        switch (id)
        {
            case 1:
                return playerPrefab;
            case 2:
                return bulletPrefab;
            default:
                Debug.LogError("Id not found or whatever");
                return null;
        }
    }
}
