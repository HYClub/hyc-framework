// =============================================================================
//  HYCSimple · 属性系统示例 · 运行时演示系统
//  仅示范 API，不接任何玩法。真实项目里实体通常挂 AttributeSetRef，由
//  AttributeInitSystem 进 Play 后自动 AddBuffer + 灌值。
//  本例**优先走真实资产链路**：首次 Update 用 AssetDatabase 加载菜单生成的
//  GameHeroSet.asset → AttributeInitializer.InitFromSet 灌袋；资产不存在时再回退到
//  手写 buffer（保证不跑菜单也能编译运行）。完整接入链路见同目录 AI-NOTES.md。
//
//  ⚠️ 刻意标了 [DisableAutoCreation]：示例 System 若自动进宿主工程的 World，会随手
//     往宿主 World 里 CreateEntity 并改写静态单例，干扰宿主。
//     改由 HYCSimpleSceneBootstrap（用户显式挂到场景才生效）手动 Update。
// =============================================================================
using Unity.Entities;
using UnityEngine;
using HYC.Framework.Attr;
using HYC.Simple.Attr.Generated;

namespace HYC.Simple
{
    /// <summary>
    /// 一次性演示：造一个带属性 buffer 的实体，用枚举版 AttrBag 读写。
    /// 用 SystemBase + 一次性 _seeded 守卫，避免每帧重复建实体。
    /// 默认不自动进 World，见 HYCSimpleSceneBootstrap。
    /// </summary>
    [DisableAutoCreation]
    public partial class HYCSimpleAttrDemoSystem : SystemBase
    {
        private bool _seeded;

        protected override void OnUpdate()
        {
            if (_seeded) return;
            _seeded = true;

            Entity e = EntityManager.CreateEntity();
            DynamicBuffer<AttributeValue> buf;

            // 真实链路：加载示例预设资产 GameHeroSet.asset → InitFromSet 灌袋。
            // 资产由菜单 HYC Framework/Samples/Attr/创建示例属性资产 生成；资产不存在时回退到手写 buffer。
#if UNITY_EDITOR
            var set = UnityEditor.AssetDatabase.LoadAssetAtPath<HYC.Framework.Attr.AttributeSetAsset>(
                "Assets/HYCSimpleSampleAssets/GameHeroSet.asset");
            if (set != null)
            {
                HYC.Framework.Attr.AttributeInitializer.InitFromSet(EntityManager, e, set);
                buf = EntityManager.GetBuffer<AttributeValue>(e);
                Debug.Log("[HYCSimple] 属性演示：走真实资产链路（InitFromSet ← GameHeroSet.asset）");
            }
            else
#endif
            {
                // 资产未创建时的回退：手写 buffer（仍演示 AttrBag 读写 API）
                buf = EntityManager.AddBuffer<AttributeValue>(e);
                buf.Add(new AttributeValue { keyHash = AttributeKey.Hash("HP"),         type = AttributeType.Int,  raw = 100 });
                buf.Add(new AttributeValue { keyHash = AttributeKey.Hash("MP"),         type = AttributeType.Int,  raw = 50 });
                buf.Add(new AttributeValue { keyHash = AttributeKey.Hash("MoveSpeed"),   type = AttributeType.Float,
                    raw = System.BitConverter.DoubleToInt64Bits(3.5) }); // Float 走 IEEE754 bits
                buf.Add(new AttributeValue { keyHash = AttributeKey.Hash("IsInvincible"), type = AttributeType.Bool, raw = 0 });
                Debug.LogWarning("[HYCSimple] 未找到 GameHeroSet.asset，回退到手写 buffer；" +
                                "请先运行菜单 HYC Framework/Samples/Attr/创建示例属性资产 生成示例资产。");
            }

            // 用枚举版 AttrBag 读（零手写 key、拼错编译期报错）
            var bag = new GameAttrBag(EntityManager, e);
            int hp = bag.GetInt(GameAttrKey.HP);                 // 100
            float speed = bag.GetFloat(GameAttrKey.MoveSpeed);   // 3.5
            bag.SetInt(GameAttrKey.HP, hp + 10);                  // 运行期 Set 不钳制（袋即运行时真相）

            // 3) 也演示 ulong 版（Job/Burst 外同样可用）
            int mp = AttributeStore.GetInt(EntityManager, e, AttributeKey.Hash("MP"));

            Debug.Log($"[HYCSimple] 属性演示：HP={hp} MP={mp} MoveSpeed={speed}");
            // 真实链路：Catalog → Set → 生成访问器 → 实体挂 AttributeSetRef
            //         → AttributeInitSystem 进 Play 自动 AddBuffer + 灌值 → 玩法用 GameAttrBag 读写。
        }
    }
}
