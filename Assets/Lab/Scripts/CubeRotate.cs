using UnityEngine;
using UnityEngine.UIElements;

public class CubeRotate : MonoBehaviour
{
    [SerializeField] private float speed = 1.0f;
    [SerializeField] private float jumpForce = 5.0f;
    [SerializeField] private float dashForce = 10.0f;

    private Rigidbody rb;
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        Vector3 movement = new Vector3(x, 0, z);
        rb.linearVelocity = new Vector3(movement.normalized.x * speed, rb.linearVelocity.y, movement.normalized.z * speed);
        if (Input.GetKeyDown(KeyCode.Space))
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }


        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            rb.AddForce(movement.normalized * dashForce, ForceMode.Impulse);
        }
     
    }
}
