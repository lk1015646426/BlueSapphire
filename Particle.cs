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

        // 构造函数：速度单位为"像素/秒"，与帧率无关
        public Particle(float screenWidth, float screenHeight, Random rand)
        {
            Position = new Vector2(
                (float)rand.NextDouble() * screenWidth,
                (float)rand.NextDouble() * screenHeight
            );

            // 速度范围 [-7.5, 7.5] 像素/秒，对应原先降帧模式下的视觉速度
            Velocity = new Vector2(
                (float)(rand.NextDouble() - 0.5) * 15.0f,
                (float)(rand.NextDouble() - 0.5) * 15.0f
            );

            Size = 2.0f;
            Color = Colors.White;
        }

        // Update：所有位移均乘以 deltaTime，确保不同帧率下速度一致
        public void Update(float screenWidth, float screenHeight, Vector2 mousePosition, float deltaTime)
        {
            // 1. 基础移动（基于时间）
            Position += Velocity * deltaTime;

            // 2. 边界反弹
            if (Position.X < 0 || Position.X > screenWidth) Velocity.X *= -1;
            if (Position.Y < 0 || Position.Y > screenHeight) Velocity.Y *= -1;

            // 3. 鼠标交互
            float dist = Vector2.Distance(Position, mousePosition);

            if (dist < 150)
            {
                var dir = Position - mousePosition;
                if (dir.LengthSquared() > 0)
                {
                    // 推力系数 0.15 对应原先降帧模式下的力度
                    var force = Vector2.Normalize(dir) * (150 - dist) * 0.15f * deltaTime;
                    Position += force;
                }
            }
        }
    }
}