using HYC.Framework.Dots;
using HYC.Simple.Input.Sense;
using HYC.Simple.Input.UI;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace HYC.Simple.Input
{
    /// <summary>
    /// 示例读取系统（表现层末，读最新鲜状态）：每 1 秒打印一名玩家的输入快照。
    /// 展示「捞出来看」——pressed=点亮、value=数值、NavMove.value=摇杆偏移。
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_B9))]
    public partial class SampleInputReaderSystem : SystemBase
    {
        private double _nextLog;
        protected override void OnUpdate()
        {
            var now = SystemAPI.Time.ElapsedTime;
            if (now < _nextLog) return;
            _nextLog = now + 1.0;

            // Entities 1.0 起 SystemAPI.Query 泛型参数须实现 IQueryTypeParameter，Entity 已不在列；
            // 要同时拿 Entity 与组件改用 EntityQuery + 数组遍历（两数组同序，按下标配对）。
            var query = GetEntityQuery(typeof(UIInputComponent), typeof(SenseInputComponent));
            var entities = query.ToEntityArray(Allocator.Temp);
            var uiArr = query.ToComponentDataArray<UIInputComponent>(Allocator.Temp);
            var senseArr = query.ToComponentDataArray<SenseInputComponent>(Allocator.Temp);
            try
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    // NativeArray<T> 索引器按值返回（Unity 2022.3 CoreModule.xml 实证：
                    // "structs are returned by value and not by reference"），
                    // 写 `ref var ui = ref uiArr[i]` 不编译；与 NativeList<T>/BlobArray<T> 不同。
                    var ui = uiArr[i];
                    var sense = senseArr[i];
                    Debug.Log($"[HYC.Simple.Input] 玩家 e={entities[i].Index} " +
                              $"Submit点亮={ui.Submit.pressed} NavMove={ui.NavMove.value} " +
                              $"Scroll={ui.Scroll.value} Look={sense.Look.value} Fire={sense.Fire.pressed}");
                }
            }
            finally
            {
                entities.Dispose();
                uiArr.Dispose();
                senseArr.Dispose();
            }
        }
    }
}
