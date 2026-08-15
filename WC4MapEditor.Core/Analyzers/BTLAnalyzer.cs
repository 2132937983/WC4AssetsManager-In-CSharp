using System.Text;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.BTL;
using WC4MapEditor.Parsers.Conquest;
using WC4MapEditor.Parsers.Stage;
using WC4MapEditor.Parsers.World;

namespace WC4MapEditor.Core.Analyzers;

public class BTLAnalyzer
{
    public string AnalyzeStageParser(StageParser parser)
    {
        var sb = new StringBuilder();

        sb.AppendLine("= BTL文件数据段地址信息 =");
        sb.AppendLine($"文件路径: {parser.HexFilePath}");
        sb.AppendLine($"文件总大小: {parser.HexData.Length:N0} 字节 (0x{parser.HexData.Length:X})");
        sb.AppendLine($"BTL版本: {parser.BtlVersion}");
        sb.AppendLine($"地图尺寸: {parser.Header.MapWidth} x {parser.Header.MapLength}");
        sb.AppendLine();

        int pos = 0;
        var h = parser.Header;

        void EmitBlock(string name, int count, int itemSize)
        {
            if (count <= 0) return;
            int blockSize = count * itemSize;
            sb.AppendLine($"【{name}】");
            sb.AppendLine($"  起始地址: 0x{pos:X8}");
            sb.AppendLine($"  结束地址: 0x{pos + blockSize - 1:X8}");
            sb.AppendLine($"  数量: {count}");
            sb.AppendLine($"  大小: {blockSize} 字节");
            sb.AppendLine();
            pos += blockSize;
        }

        // BTLHeader 字段详情（偏移 + 值）
        sb.AppendLine("【BTLHeader】 0x00 - 0x7F (128字节)");
        sb.AppendLine($"  0x00 BtlVersion:          {h.BtlVersion}");
        sb.AppendLine($"  0x04 MapNumber:           {h.MapNumber}");
        sb.AppendLine($"  0x08 MapClipX:            {h.MapClipX}");
        sb.AppendLine($"  0x0C MapClipY:            {h.MapClipY}");
        sb.AppendLine($"  0x10 MapLength:           {h.MapLength}");
        sb.AppendLine($"  0x14 MapWidth:            {h.MapWidth}");
        sb.AppendLine($"  0x18 ArmyCount:           {h.ArmyCount}");
        sb.AppendLine($"  0x1C BuildingCount:       {h.BuildingCount}");
        sb.AppendLine($"  0x20 TroopCount:          {h.TroopCount}");
        sb.AppendLine($"  0x24 PlanCount:           {h.PlanCount}");
        sb.AppendLine($"  0x28 EventCount:          {h.EventCount}");
        sb.AppendLine($"  0x2C WeatherCount:        {h.WeatherCount}");
        sb.AppendLine($"  0x30 VictoryCondition:    {h.VictoryCondition}");
        sb.AppendLine($"  0x34 MinTurns:            {h.MinTurns}");
        sb.AppendLine($"  0x38 MaxTurns:            {h.MaxTurns}");
        sb.AppendLine($"  0x3C ReinforcementCount:  {h.ReinforcementCount}");
        sb.AppendLine($"  0x40 AirRaidCount:        {h.AirRaidCount}");
        sb.AppendLine($"  0x44 PlacementA:          {h.PlacementA}");
        sb.AppendLine($"  0x48 PlacementB:          {h.PlacementB}");
        sb.AppendLine($"  0x4C ConqueredFlagPosition: {h.ConqueredFlagPosition}");
        sb.AppendLine($"  0x50 Unknown3:            {h.Unknown3}");
        sb.AppendLine($"  0x54 Unknown4:            {h.Unknown4}");
        sb.AppendLine($"  0x58 SelectableTileCount: {h.SelectableTileCount}");
        sb.AppendLine($"  0x5C AccumulatedEconomy:  {h.AccumulatedEconomy}");
        sb.AppendLine($"  0x60 AccumulatedIndustry: {h.AccumulatedIndustry}");
        sb.AppendLine($"  0x64 AccumulatedTech:     {h.AccumulatedTech}");
        sb.AppendLine($"  0x68 TrapCount:           {h.TrapCount}");
        sb.AppendLine($"  0x6C Unknown5:            {h.Unknown5}");
        sb.AppendLine($"  0x70 StrategyCount:       {h.StrategyCount}");
        sb.AppendLine($"  0x74 Unknown6:            {h.Unknown6}");
        sb.AppendLine($"  0x78 Unknown7:            {h.Unknown7}");
        sb.AppendLine($"  0x7C AirSupportCount:     {h.AirSupportCount}");
        sb.AppendLine();
        pos += 128;

        EmitBlock("军团数据", h.ArmyCount, 300);
        EmitBlock("地形数据", h.SelectableTileCount, 16);
        EmitBlock("省份数据", h.SelectableTileCount, 2);
        EmitBlock("归属数据", h.SelectableTileCount, 1);
        EmitBlock("建筑数据", h.BuildingCount, 32);
        int armySize = BTLArmyModule.GetArmySize(parser.BtlVersion);
        EmitBlock("兵种数据", h.TroopCount, armySize);
        EmitBlock("陷阱数据", h.TrapCount, 12);
        EmitBlock("方案数据", h.PlanCount, 16);
        EmitBlock("天气数据", h.WeatherCount, 16);
        EmitBlock("事件数据", h.EventCount, 44);
        int reinSize = BTLReinforcementModule.GetReinforcementSize(parser.BtlVersion);
        EmitBlock("援军数据", h.ReinforcementCount, reinSize);
        EmitBlock("空袭数据", h.AirRaidCount, 20);
        EmitBlock("单位位置数据A", h.PlacementA, 8);
        EmitBlock("单位位置数据B", h.PlacementB, 8);
        EmitBlock("首都数据", h.ConqueredFlagPosition, 4);
        EmitBlock("战略建设数据", h.StrategyCount, 16);
        EmitBlock("空中支援数据", h.AirSupportCount, 16);

        sb.AppendLine($"=== 已解析数据总大小: {pos:N0} 字节 ===");
        if (pos != parser.HexData.Length)
            sb.AppendLine($"  (剩余未解析: {Math.Abs(parser.HexData.Length - pos)} 字节)");

        sb.AppendLine();
        sb.AppendLine("=== 地址计算说明 ===");
        sb.AppendLine($"  头部: 0x00 - 0x7F (128字节)");
        sb.AppendLine($"  军团: 每个300字节");
        sb.AppendLine($"  地形: 每个16字节");
        sb.AppendLine($"  省份: 每个2字节");
        sb.AppendLine($"  归属: 每个1字节");
        sb.AppendLine($"  建筑: 每个32字节 (征服文件每48字节)");
        sb.AppendLine($"  兵种: 每个48字节 (v1) / 每个64字节 (v3)");
        sb.AppendLine($"  陷阱: 每个12字节");
        sb.AppendLine($"  方案: 每个16字节");
        sb.AppendLine($"  天气: 每个16字节");
        sb.AppendLine($"  事件: 每个44字节");
        sb.AppendLine($"  援军: 每个80字节 (v1) / 每个104字节 (v3)");
        sb.AppendLine($"  空袭: 每个20字节");
        sb.AppendLine($"  单位位置: 每个8字节");
        sb.AppendLine($"  首都: 每个4字节");
        sb.AppendLine($"  战略建设: 每个16字节 (征服文件每个16字节)");
        sb.AppendLine($"  空中支援: 每个16字节");

        return sb.ToString();
    }

    public string AnalyzeConquestParser(ConquestParser parser)
    {
        var sb = new StringBuilder();

        sb.AppendLine("= BTL文件数据段地址信息 =");
        sb.AppendLine($"文件路径: {parser.HexFilePath}");
        sb.AppendLine($"文件总大小: {parser.HexData.Length:N0} 字节 (0x{parser.HexData.Length:X})");
        sb.AppendLine($"BTL版本: {parser.BtlVersion}");
        sb.AppendLine($"地图尺寸: {parser.Header.MapWidth} x {parser.Header.MapLength}");
        sb.AppendLine();

        int pos = 0;
        var h = parser.Header;

        void EmitBlock(string name, int count, int itemSize)
        {
            if (count <= 0) return;
            int blockSize = count * itemSize;
            sb.AppendLine($"【{name}】");
            sb.AppendLine($"  起始地址: 0x{pos:X8}");
            sb.AppendLine($"  结束地址: 0x{pos + blockSize - 1:X8}");
            sb.AppendLine($"  数量: {count}");
            sb.AppendLine($"  大小: {blockSize} 字节");
            sb.AppendLine();
            pos += blockSize;
        }

        // BTLHeader 字段详情（偏移 + 值）
        sb.AppendLine("【BTLHeader】 0x00 - 0x7F (128字节)");
        sb.AppendLine($"  0x00 BtlVersion:          {h.BtlVersion}");
        sb.AppendLine($"  0x04 MapNumber:           {h.MapNumber}");
        sb.AppendLine($"  0x08 MapClipX:            {h.MapClipX}");
        sb.AppendLine($"  0x0C MapClipY:            {h.MapClipY}");
        sb.AppendLine($"  0x10 MapLength:           {h.MapLength}");
        sb.AppendLine($"  0x14 MapWidth:            {h.MapWidth}");
        sb.AppendLine($"  0x18 ArmyCount:           {h.ArmyCount}");
        sb.AppendLine($"  0x1C BuildingCount:       {h.BuildingCount}");
        sb.AppendLine($"  0x20 TroopCount:          {h.TroopCount}");
        sb.AppendLine($"  0x24 PlanCount:           {h.PlanCount}");
        sb.AppendLine($"  0x28 EventCount:          {h.EventCount}");
        sb.AppendLine($"  0x2C WeatherCount:        {h.WeatherCount}");
        sb.AppendLine($"  0x30 VictoryCondition:    {h.VictoryCondition}");
        sb.AppendLine($"  0x34 MinTurns:            {h.MinTurns}");
        sb.AppendLine($"  0x38 MaxTurns:            {h.MaxTurns}");
        sb.AppendLine($"  0x3C ReinforcementCount:  {h.ReinforcementCount}");
        sb.AppendLine($"  0x40 AirRaidCount:        {h.AirRaidCount}");
        sb.AppendLine($"  0x44 PlacementA:          {h.PlacementA}");
        sb.AppendLine($"  0x48 PlacementB:          {h.PlacementB}");
        sb.AppendLine($"  0x4C ConqueredFlagPosition: {h.ConqueredFlagPosition}");
        sb.AppendLine($"  0x50 Unknown3:            {h.Unknown3}");
        sb.AppendLine($"  0x54 Unknown4:            {h.Unknown4}");
        sb.AppendLine($"  0x58 SelectableTileCount: {h.SelectableTileCount}");
        sb.AppendLine($"  0x5C AccumulatedEconomy:  {h.AccumulatedEconomy}");
        sb.AppendLine($"  0x60 AccumulatedIndustry: {h.AccumulatedIndustry}");
        sb.AppendLine($"  0x64 AccumulatedTech:     {h.AccumulatedTech}");
        sb.AppendLine($"  0x68 TrapCount:           {h.TrapCount}");
        sb.AppendLine($"  0x6C Unknown5:            {h.Unknown5}");
        sb.AppendLine($"  0x70 StrategyCount:       {h.StrategyCount}");
        sb.AppendLine($"  0x74 Unknown6:            {h.Unknown6}");
        sb.AppendLine($"  0x78 Unknown7:            {h.Unknown7}");
        sb.AppendLine($"  0x7C AirSupportCount:     {h.AirSupportCount}");
        sb.AppendLine();
        pos += 128;

        EmitBlock("军团数据", h.ArmyCount, 300);
        EmitBlock("省份数据", h.SelectableTileCount, 2);
        EmitBlock("归属数据", h.SelectableTileCount, 1);
        EmitBlock("建筑数据", h.BuildingCount, 32);
        int armySize = BTLArmyModule.GetArmySize(parser.BtlVersion);
        EmitBlock("兵种数据", h.TroopCount, armySize);
        EmitBlock("陷阱数据", h.TrapCount, 12);
        EmitBlock("方案数据", h.PlanCount, 16);
        EmitBlock("天气数据", h.WeatherCount, 16);
        EmitBlock("事件数据", h.EventCount, 44);
        int reinSize = BTLReinforcementModule.GetReinforcementSize(parser.BtlVersion);
        EmitBlock("援军数据", h.ReinforcementCount, reinSize);
        EmitBlock("空袭数据", h.AirRaidCount, 20);
        EmitBlock("单位位置数据A", h.PlacementA, 4);
        EmitBlock("单位位置数据B", h.PlacementB, 4);
        EmitBlock("首都数据", h.ConqueredFlagPosition, 4);
        EmitBlock("战略建设数据", h.StrategyCount, 16);
        EmitBlock("空中支援数据", h.AirSupportCount, 16);

        sb.AppendLine($"=== 已解析数据总大小: {pos:N0} 字节 ===");
        if (pos != parser.HexData.Length)
            sb.AppendLine($"  (剩余未解析: {Math.Abs(parser.HexData.Length - pos)} 字节)");

        sb.AppendLine();
        sb.AppendLine("=== 地址计算说明 ===");
        sb.AppendLine($"  头部: 0x00 - 0x7F (128字节)");
        sb.AppendLine($"  军团: 每个300字节");
        sb.AppendLine($"  省份: 每个2字节");
        sb.AppendLine($"  归属: 每个1字节");
        sb.AppendLine($"  建筑: 每个32字节");
        sb.AppendLine($"  兵种: 每个48字节 (v1) / 每个64字节 (v3)");
        sb.AppendLine($"  陷阱: 每个12字节");
        sb.AppendLine($"  方案: 每个16字节");
        sb.AppendLine($"  天气: 每个16字节");
        sb.AppendLine($"  事件: 每个44字节");
        sb.AppendLine($"  援军: 每个80字节 (v1) / 每个104字节 (v3)");
        sb.AppendLine($"  空袭: 每个20字节");
        sb.AppendLine($"  单位位置: 每个4字节");
        sb.AppendLine($"  首都: 每个4字节");
        sb.AppendLine($"  战略建设: 每个16字节");
        sb.AppendLine($"  空中支援: 每个16字节");

        return sb.ToString();
    }

    public string AnalyzeWorldParser(MapData mapData)
    {
        var sb = new StringBuilder();

        sb.AppendLine("= 世界文件数据段地址信息 =");
        sb.AppendLine($"文件路径: {mapData.FilePath}");
        sb.AppendLine($"地图尺寸: {mapData.MapWidth} x {mapData.MapHeight}");
        sb.AppendLine($"地形总数: {mapData.TerrainCount}");
        sb.AppendLine();

        int baseOffset = 16;
        int terrainBlockSize = mapData.TerrainCount * 16;
        var h = mapData.Header;

        sb.AppendLine("【BTLHeader】 0x00 - 0x0F (16字节)");
        sb.AppendLine($"  0x00 BtlVersion:    {h.BtlVersion}");
        sb.AppendLine($"  0x04 MapNumber:     {h.MapNumber}");
        sb.AppendLine($"  0x08 MapClipX:      {h.MapClipX}");
        sb.AppendLine($"  0x0C MapClipY:      {h.MapClipY}");
        sb.AppendLine();

        sb.AppendLine($"【地形数据】");
        sb.AppendLine($"  起始地址: 0x{baseOffset:X8}");
        sb.AppendLine($"  结束地址: 0x{baseOffset + terrainBlockSize - 1:X8}");
        sb.AppendLine($"  数量: {mapData.TerrainCount}");
        sb.AppendLine($"  大小: {terrainBlockSize:N0} 字节");
        sb.AppendLine();

        return sb.ToString();
    }
}
