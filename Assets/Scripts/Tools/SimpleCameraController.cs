using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    public float moveSpeed = 5f; // 相机移动速度

    void Update()
    {
        // 获取按键输入
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        // 计算移动方向
        Vector3 moveDirection = new Vector3(horizontalInput, 0f, verticalInput).normalized;

        // 移动相机
        transform.Translate(moveDirection * moveSpeed * Time.deltaTime);
    }
}