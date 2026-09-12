// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTNodeType.cs
// 说明: 行为树节点类型枚举(通用底层, 任何游戏复用)
// ============================================================

namespace HYC.Framework.BT
{
    /// <summary>
    /// 行为树节点类型。节点是纯数据(Blob), 由解释器按类型分发执行。
    /// 组合/装饰为结构节点, 条件/动作为叶子节点。
    /// </summary>
    // ============================================================
    // 参数约定(Long 池): 未列出 Long 参数的节点沿用原有语义。
    // 组合节点的状态(上次运行子索引 / finished 掩码 / 重复计数)由
    // BTNodeRuntimeState 持久化, 见 BTInterpreter 与 BTManager.GetNodeStates。
    // ============================================================
    public enum BTNodeType : byte
    {
        // ---- 入口/出口 ----
        Root,           // 树的唯一入口, 转发其唯一子节点结果
        End,            // 显式终止, 返回 Success(技能流程提前结束用)

        // ---- 组合节点(有子节点) ----
        // Long[0] bit0 = dynamic: 每帧从 0 重估高优先级子节点, 切换时复位被顶掉的子节点分支
        Sequence,       // 顺序执行, 子节点 Failed 则短路
        Selector,       // 选择执行, 子节点 Success 则短路
        // Long[0] = policy(0=FirstFailure 1=FirstSuccess 2=FirstSuccessOrFailure)
        // Long[1] = dynamic(已完成子节点重复执行); finished 集合由 MaskA 位掩码持久化
        Parallel,       // 并行执行, 按策略汇总
        RandomSelector, // 随机顺序选择执行(种子持久化, 不会每帧重洗)
        RandomSequence, // 随机顺序顺序执行(种子持久化, 不会每帧重洗)

        // ---- 装饰节点(包一个子节点) ----
        Invert,         // 结果取反
        // Long[0] = 重复次数(<=0 表示无限); 当前迭代数持久化, 支持 Running 子节点跨帧续跑
        Repeat,         // 重复 N 次
        UntilSuccess,   // 循环直到成功(跨帧: 成功则成功, 否则持续 Running 下一帧重试)
        UntilFail,      // 循环直到失败(跨帧: 失败则成功, 否则持续 Running 下一帧重试)
        AlwaysSuccess,  // 强制成功
        AlwaysFail,     // 强制失败
        CooldownGate,   // 冷却门(冷却期内返回 Failed)
        // child0 = 条件, child1 = 被包子树
        // Long[0] = 条件不满足时返回的状态(默认 Failed, 可为 Success/Optional)
        // Long[1] = dynamic: 每帧重估条件, 条件变假即中断子树
        Conditional,    // 条件装饰(NodeCanvas ConditionalEvaluator 语义)
        TimeLimit,      // 超时限制: 子树运行超过 N 秒强制 Failed

        // ---- 条件节点(叶子, 判断) ----
        CheckDistance,  // 距离判断(黑板值 vs 阈值)
        CheckBlackboard,// 黑板值判断(bool/比较)

        // ---- 动作节点(叶子, 干活) ----
        Wait,           // 等待指定秒数(Running)
        NoOp,           // 空操作

        // ---- 特殊 ----
        SubTree,        // 子树引用: 执行另一棵树(参数 Long[0]=目标树ID)

        // ---- 新增组合节点(NodeCanvas 移植, P2) ----
        // 追加在 SubTree 之后: 保持已有枚举数值不变, 避免旧 BTTreeAsset 序列化错位
        // 旋转起点持久化在 Current; 成功的子节点"移到末尾"= 起点+1
        FlipSelector,        // 成功子节点移到末尾(失败过的优先再试)
        // 权重 Float[0..N-1](Long[0..N-1] 为黑板键, 0=字面); Long[N]=dynamic
        UtilitySelector,     // 工具AI: 按权重(utility)最高的子节点执行
        // 权重同上; Long[N]=failChance 的黑板键/字面在 Float 末位; dice 存 Elapsed, 失败标记存 MaskA
        ProbabilitySelector, // 按权重概率选子节点, 失败则换下一个
        // 每次执行只跑一个子节点并返回其状态; 子节点终结后步进到下一个(存 Iteration)
        StepSequencer,       // 逐步顺序: 一次执行一个子节点
        // Long[0]=case 黑板键(0=用 Long[1] 字面), Long[2]=dynamic, Long[3]=越界模式
        Switch,              // 按 int case 选择一个子节点执行
        // child0=条件, child1=条件真, child2=条件假; Long[0]=dynamic
        BinarySelector,      // 二选一(按条件子树结果)

        // ---- 新增装饰节点(NodeCanvas 移植, P3) ----
        // Float[0]=timeout 秒(Long[0]=黑板键); 子节点 Running 超时则复位并 Failed
        Timeout,
        // child0=条件, child1=被包子树(可选); 条件未满足前一直 Running
        WaitUntil,
        // 跨分支令牌互斥: token 取自黑板字符串(Long[0]=键)或字面(String[0]); Long[1]=被占用时(0=Failed 1=Running等待)
        Guard,
        // child0=条件, child1=被包; 条件为真时中断子树并 Failed
        Interruptor,
        // Long[0]=模式(0=冷却 1=限次); Float[0]=冷却秒/次数(Long[1]=键); Long[2]=policy; Long[3]=被过滤时返 Optional
        Filter,
        // Long[0]=列表长度键(0=用 Long[1] 字面); Long[2]=终止策略; Long[3]=当前索引写入键
        Iterator,
        // child0=被包, child1=监测动作; Long[0]=监测状态(0失败 1成功 10任意); Long[1]=返回模式(0原始 1动作)
        Monitor,
        // 执行子节点但返回 Optional(父组合忽略其成败)
        Optional,
        // Long[0]=成功重映射(0=Failed 1=Success); Long[1]=失败重映射
        Remapper,

        // ---- 事件机制(G3, 对应 NodeCanvas SendEvent / CheckEvent) ----
        // Long[0]=事件 key 哈希; Long[1]=范围(0=本实体 1=全局)
        SendEvent,   // 抛事件(动作)
        // Long[0]=事件 key 哈希; Long[1]=是否消费(0=只查询 1=消费后返回成功)
        CheckEvent,  // 判断事件是否已抛出(条件)

        // ---- 扩展节点(由游戏层注册, 0x80 起) ----
        GameCustom = 128,
    }

    /// <summary>树资产类型: 技能树 / 角色AI树 / 其他(用于节点语义注册分区)。</summary>
    public enum BTTreeKind
    {
        Skill = 0,
        AI = 1,
        Other = 2,
    }
}
