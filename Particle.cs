using Microsoft.UI;
using System; // 需要引用 System 以使用 Random
using System.Numerics;
using Windows.UI;

namespace BlueSapphire
{
    public class Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Size;
        public Color Color;

        // 【关键修改 1】构造函数适配 MainWindow
        // MainWindow 调用的是: new Particle(width, height, random)
        public Particle(float screenWidth, float screenHeight, Random rand)
        {
            // 在这里内部计算随机位置
            Position = new Vector2(
                (float)rand.NextDouble() * screenWidth,
                (float)rand.NextDouble() * screenHeight
            );

            // 内部计算随机速度 (-1 到 1 之间)
            Velocity = new Vector2(
                (float)(rand.NextDouble() - 0.5) * 2.0f,
                (float)(rand.NextDouble() - 0.5) * 2.0f
            );

            // 设置默认值
            Size = 2.0f;
            Color = Colors.White; // 默认颜色，具体绘制时由 OnDraw 控制
        }

        // 【关键修改 2】Update 方法适配 MainWindow
        // MainWindow 调用的是: Update(width, height, mousePos)
        public void Update(float screenWidth, float screenHeight, Vector2 mousePosition)
        {
            // 1. 基础移动
            Position += Velocity;

            // 2. 边界反弹
            if (Position.X < 0 || Position.X > screenWidth) Velocity.X *= -1;
            if (Position.Y < 0 || Position.Y > screenHeight) Velocity.Y *= -1;

            // 3. 鼠标交互 (新增逻辑)
            // 计算粒子与鼠标的距离
            float dist = Vector2.Distance(Position, mousePosition);

            // 如果距离小于 150 像素，产生排斥力
            if (dist < 150)
            {
                var dir = Position - mousePosition;
                if (dir.LengthSquared() > 0) // 防止除以0
                {
                    // 简单的物理推力模拟
                    var force = Vector2.Normalize(dir) * (150 - dist) * 0.02f;
                    Position += force;
                }
            }
        }
    }
}