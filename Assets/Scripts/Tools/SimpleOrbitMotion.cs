using UnityEngine;

public class SimpleOrbitMotion : MonoBehaviour
{
    public Transform centerObject; // 中心物体的Transform组件
    public float rotationSpeed = 1f; // 旋转速度

    private void Update()
    {
        // 计算旋转轴和旋转角度
        Vector3 rotationAxis = Vector3.up; // 可以根据需要修改旋转轴
        float rotationAngle = rotationSpeed * Time.deltaTime;

        // 围绕中心物体旋转
        transform.RotateAround(centerObject.position, rotationAxis, rotationAngle);
    }
}