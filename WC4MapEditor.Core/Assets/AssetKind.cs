namespace WC4MapEditor.Core.Assets;

/// <summary>
/// 资源语义类别。用于按用途查询文件，而不仅仅按扩展名。
/// </summary>
public enum AssetKind
{
    /// <summary>未分类 / 其它。</summary>
    Unknown = 0,

    // ---- stage 目录下的 .btl 关卡文件，按文件名前缀细分 ----
    /// <summary>战役关卡：stage 目录下 stageXXXX.btl。</summary>
    Stage,
    /// <summary>征服：conquestN.btl。</summary>
    Conquest,
    /// <summary>事件关卡：eventXXXX.btl。</summary>
    Event,
    /// <summary>前线：frontierXXXX.btl。</summary>
    Frontier,
    /// <summary>名将关卡：generalstage_XXX.btl。</summary>
    GeneralStage,
    /// <summary>传奇：legendXXXX.btl。</summary>
    Legend,
    /// <summary>战区：warzoneXXX.btl。</summary>
    Warzone,
    /// <summary>入侵军团：invadecorpsN.btl。</summary>
    InvadeCorps,
    /// <summary>其它 .btl 关卡文件。</summary>
    OtherStageBtl,

    // ---- 按内容类型分类的通用资源 ----
    /// <summary>纹理 / 图片（png/webp/pkm/jpg 等）。</summary>
    Texture,
    /// <summary>XML 描述文件。</summary>
    Xml,
    /// <summary>JSON 配置文件。</summary>
    Json,
    /// <summary>二进制数据（.bin）。</summary>
    Binary,
    /// <summary>音频文件。</summary>
    Audio,
    /// <summary>字体文件。</summary>
    Font,
    /// <summary>着色器文件。</summary>
    Shader,
    /// <summary>字符串表 / ini 文本。</summary>
    StringTable,
}
