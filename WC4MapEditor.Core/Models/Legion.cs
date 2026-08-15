using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

/// <summary>
/// 军团数据结构 (300字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Legion
{
    public int ActionId;
    public int CountryId;
    public int InitialEconomy;
    public int InitialIndustry;
    public int InitialTech;
    public int IsPlayerControlled;
    public int Camp;
    public int DefeatCondition;
    public float CountryHpRate;
    public float CountryTaxRate;
    public byte ColorR;
    public byte ColorG;
    public byte ColorB;
    public byte ReservedColor;
    public int AtomicBombCount;
    public int HydrogenBombCount;
    public int NeutronBombCount;
    public int AntimatterBombCount;
    public int MobilityLevel;
    public int RifleLevel;
    public int CamouflageLevel;
    public int EngineerLevel;
    public int GrenadeLevel;
    public int MortarLevel;
    public int MarchLevel;
    public int BulletproofVestLevel;
    public int ArmorLevel;
    public int MainGunLevel;
    public int VehicleBodyLevel;
    public int EngineLevel;
    public int MachineGunLevel;
    public int RaidLevel;
    public int VehicleAirDefenseLevel;
    public int ReinforcedBodyLevel;
    public int ArtilleryLevel;
    public int RocketLevel;
    public int TowingLevel;
    public int ArtilleryArmorLevel;
    public int FirepowerLevel;
    public int NavyRocketLevel;
    public int DisguiseLevel;
    public int HullLevel;
    public int PropulsionLevel;
    public int NavyArmorLevel;
    public int WeaponLevel;
    public int NavalGunLevel;
    public int TorpedoLevel;
    public int MinesweepingLevel;
    public int ShipAirDefenseLevel;
    public int ModernHullLevel;
    public int AviationFuelLevel;
    public int AviationEngineLevel;
    public int AviationBombLevel;
    public int AirRaidLevel;
    public int BombingLevel;
    public int StrategicBombingLevel;
    public int AirdropLevel;
    public int JetEngineLevel;
    public int BunkerLevel;
    public int FortressGunLevel;
    public int CoastalGunLevel;
    public int RocketLauncherLevel;
    public int FortificationLevel;
    public int AntiAircraftGunLevel;
    public int AntiAircraftCannonLevel;
    public int AntiAircraftMissileLevel;
    public int RadarLevel;
    public int MissileWarheadLevel;
    public int SolidRocketEngineLevel;
    public int NuclearBombBreakthroughLevel;
    public int NuclearFusionLevel;
    public int Unknown1;
    public int Unknown2;
    public int Unknown3;
    public int CountryAiBehavior;
    public int InitialTechLevel;
    public int Unknown4;
    public int InitialLaserGunLevel;

    public readonly bool IsPlayer => IsPlayerControlled != 0;
    public readonly string DisplayName => $"军团 {CountryId}";

    /// <summary>
    /// 从字节数组解析军团数据
    /// </summary>
    public static Legion FromBytes(ReadOnlySpan<byte> data, int offset)
    {
        if (data.Length < offset + 300) return default;

        return new Legion
        {
            ActionId = BitConverter.ToInt32(data[(offset + 0x00)..]),
            CountryId = BitConverter.ToInt32(data[(offset + 0x04)..]),
            InitialEconomy = BitConverter.ToInt32(data[(offset + 0x08)..]),
            InitialIndustry = BitConverter.ToInt32(data[(offset + 0x0C)..]),
            InitialTech = BitConverter.ToInt32(data[(offset + 0x10)..]),
            IsPlayerControlled = BitConverter.ToInt32(data[(offset + 0x14)..]),
            Camp = BitConverter.ToInt32(data[(offset + 0x18)..]),
            DefeatCondition = BitConverter.ToInt32(data[(offset + 0x1C)..]),
            CountryHpRate = BitConverter.ToSingle(data[(offset + 0x20)..]),
            CountryTaxRate = BitConverter.ToSingle(data[(offset + 0x24)..]),
            ColorR = data[offset + 0x28],
            ColorG = data[offset + 0x29],
            ColorB = data[offset + 0x2A],
            AtomicBombCount = BitConverter.ToInt32(data[(offset + 0x2C)..]),
            HydrogenBombCount = BitConverter.ToInt32(data[(offset + 0x30)..]),
            NeutronBombCount = BitConverter.ToInt32(data[(offset + 0x34)..]),
            AntimatterBombCount = BitConverter.ToInt32(data[(offset + 0x38)..]),
            MobilityLevel = BitConverter.ToInt32(data[(offset + 0x3C)..]),
            RifleLevel = BitConverter.ToInt32(data[(offset + 0x40)..]),
            CamouflageLevel = BitConverter.ToInt32(data[(offset + 0x44)..]),
            EngineerLevel = BitConverter.ToInt32(data[(offset + 0x48)..]),
            GrenadeLevel = BitConverter.ToInt32(data[(offset + 0x4C)..]),
            MortarLevel = BitConverter.ToInt32(data[(offset + 0x50)..]),
            MarchLevel = BitConverter.ToInt32(data[(offset + 0x54)..]),
            BulletproofVestLevel = BitConverter.ToInt32(data[(offset + 0x58)..]),
            ArmorLevel = BitConverter.ToInt32(data[(offset + 0x5C)..]),
            MainGunLevel = BitConverter.ToInt32(data[(offset + 0x60)..]),
            VehicleBodyLevel = BitConverter.ToInt32(data[(offset + 0x64)..]),
            EngineLevel = BitConverter.ToInt32(data[(offset + 0x68)..]),
            MachineGunLevel = BitConverter.ToInt32(data[(offset + 0x6C)..]),
            RaidLevel = BitConverter.ToInt32(data[(offset + 0x70)..]),
            VehicleAirDefenseLevel = BitConverter.ToInt32(data[(offset + 0x74)..]),
            ReinforcedBodyLevel = BitConverter.ToInt32(data[(offset + 0x78)..]),
            ArtilleryLevel = BitConverter.ToInt32(data[(offset + 0x7C)..]),
            RocketLevel = BitConverter.ToInt32(data[(offset + 0x80)..]),
            TowingLevel = BitConverter.ToInt32(data[(offset + 0x84)..]),
            ArtilleryArmorLevel = BitConverter.ToInt32(data[(offset + 0x88)..]),
            FirepowerLevel = BitConverter.ToInt32(data[(offset + 0x8C)..]),
            NavyRocketLevel = BitConverter.ToInt32(data[(offset + 0x90)..]),
            DisguiseLevel = BitConverter.ToInt32(data[(offset + 0x94)..]),
            HullLevel = BitConverter.ToInt32(data[(offset + 0x98)..]),
            PropulsionLevel = BitConverter.ToInt32(data[(offset + 0x9C)..]),
            NavyArmorLevel = BitConverter.ToInt32(data[(offset + 0xA0)..]),
            WeaponLevel = BitConverter.ToInt32(data[(offset + 0xA4)..]),
            NavalGunLevel = BitConverter.ToInt32(data[(offset + 0xA8)..]),
            TorpedoLevel = BitConverter.ToInt32(data[(offset + 0xAC)..]),
            MinesweepingLevel = BitConverter.ToInt32(data[(offset + 0xB0)..]),
            ShipAirDefenseLevel = BitConverter.ToInt32(data[(offset + 0xB4)..]),
            ModernHullLevel = BitConverter.ToInt32(data[(offset + 0xB8)..]),
            AviationFuelLevel = BitConverter.ToInt32(data[(offset + 0xBC)..]),
            AviationEngineLevel = BitConverter.ToInt32(data[(offset + 0xC0)..]),
            AviationBombLevel = BitConverter.ToInt32(data[(offset + 0xC4)..]),
            AirRaidLevel = BitConverter.ToInt32(data[(offset + 0xC8)..]),
            BombingLevel = BitConverter.ToInt32(data[(offset + 0xCC)..]),
            StrategicBombingLevel = BitConverter.ToInt32(data[(offset + 0xD0)..]),
            AirdropLevel = BitConverter.ToInt32(data[(offset + 0xD4)..]),
            JetEngineLevel = BitConverter.ToInt32(data[(offset + 0xD8)..]),
            BunkerLevel = BitConverter.ToInt32(data[(offset + 0xDC)..]),
            FortressGunLevel = BitConverter.ToInt32(data[(offset + 0xE0)..]),
            CoastalGunLevel = BitConverter.ToInt32(data[(offset + 0xE4)..]),
            RocketLauncherLevel = BitConverter.ToInt32(data[(offset + 0xE8)..]),
            FortificationLevel = BitConverter.ToInt32(data[(offset + 0xEC)..]),
            AntiAircraftGunLevel = BitConverter.ToInt32(data[(offset + 0xF0)..]),
            AntiAircraftCannonLevel = BitConverter.ToInt32(data[(offset + 0xF4)..]),
            AntiAircraftMissileLevel = BitConverter.ToInt32(data[(offset + 0xF8)..]),
            RadarLevel = BitConverter.ToInt32(data[(offset + 0xFC)..]),
            MissileWarheadLevel = BitConverter.ToInt32(data[(offset + 0x100)..]),
            SolidRocketEngineLevel = BitConverter.ToInt32(data[(offset + 0x104)..]),
            NuclearBombBreakthroughLevel = BitConverter.ToInt32(data[(offset + 0x108)..]),
            NuclearFusionLevel = BitConverter.ToInt32(data[(offset + 0x10C)..]),
            Unknown1 = BitConverter.ToInt32(data[(offset + 0x110)..]),
            Unknown2 = BitConverter.ToInt32(data[(offset + 0x114)..]),
            Unknown3 = BitConverter.ToInt32(data[(offset + 0x118)..]),
            CountryAiBehavior = BitConverter.ToInt32(data[(offset + 0x11C)..]),
            InitialTechLevel = BitConverter.ToInt32(data[(offset + 0x120)..]),
            Unknown4 = BitConverter.ToInt32(data[(offset + 0x124)..]),
            InitialLaserGunLevel = BitConverter.ToInt32(data[(offset + 0x128)..])
        };
    }

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset)
    {
        BitConverter.TryWriteBytes(data[(offset + 0x00)..], ActionId);
        BitConverter.TryWriteBytes(data[(offset + 0x04)..], CountryId);
        BitConverter.TryWriteBytes(data[(offset + 0x08)..], InitialEconomy);
        BitConverter.TryWriteBytes(data[(offset + 0x0C)..], InitialIndustry);
        BitConverter.TryWriteBytes(data[(offset + 0x10)..], InitialTech);
        BitConverter.TryWriteBytes(data[(offset + 0x14)..], IsPlayerControlled);
        BitConverter.TryWriteBytes(data[(offset + 0x18)..], Camp);
        BitConverter.TryWriteBytes(data[(offset + 0x1C)..], DefeatCondition);
        BitConverter.TryWriteBytes(data[(offset + 0x20)..], CountryHpRate);
        BitConverter.TryWriteBytes(data[(offset + 0x24)..], CountryTaxRate);
        data[offset + 0x28] = ColorR;
        data[offset + 0x29] = ColorG;
        data[offset + 0x2A] = ColorB;
        BitConverter.TryWriteBytes(data[(offset + 0x2C)..], AtomicBombCount);
        BitConverter.TryWriteBytes(data[(offset + 0x30)..], HydrogenBombCount);
        BitConverter.TryWriteBytes(data[(offset + 0x34)..], NeutronBombCount);
        BitConverter.TryWriteBytes(data[(offset + 0x38)..], AntimatterBombCount);
        BitConverter.TryWriteBytes(data[(offset + 0x3C)..], MobilityLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x40)..], RifleLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x44)..], CamouflageLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x48)..], EngineerLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x4C)..], GrenadeLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x50)..], MortarLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x54)..], MarchLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x58)..], BulletproofVestLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x5C)..], ArmorLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x60)..], MainGunLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x64)..], VehicleBodyLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x68)..], EngineLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x6C)..], MachineGunLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x70)..], RaidLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x74)..], VehicleAirDefenseLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x78)..], ReinforcedBodyLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x7C)..], ArtilleryLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x80)..], RocketLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x84)..], TowingLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x88)..], ArtilleryArmorLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x8C)..], FirepowerLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x90)..], NavyRocketLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x94)..], DisguiseLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x98)..], HullLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x9C)..], PropulsionLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xA0)..], NavyArmorLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xA4)..], WeaponLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xA8)..], NavalGunLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xAC)..], TorpedoLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xB0)..], MinesweepingLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xB4)..], ShipAirDefenseLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xB8)..], ModernHullLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xBC)..], AviationFuelLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xC0)..], AviationEngineLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xC4)..], AviationBombLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xC8)..], AirRaidLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xCC)..], BombingLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xD0)..], StrategicBombingLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xD4)..], AirdropLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xD8)..], JetEngineLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xDC)..], BunkerLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xE0)..], FortressGunLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xE4)..], CoastalGunLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xE8)..], RocketLauncherLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xEC)..], FortificationLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xF0)..], AntiAircraftGunLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xF4)..], AntiAircraftCannonLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xF8)..], AntiAircraftMissileLevel);
        BitConverter.TryWriteBytes(data[(offset + 0xFC)..], RadarLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x100)..], MissileWarheadLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x104)..], SolidRocketEngineLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x108)..], NuclearBombBreakthroughLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x10C)..], NuclearFusionLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x110)..], Unknown1);
        BitConverter.TryWriteBytes(data[(offset + 0x114)..], Unknown2);
        BitConverter.TryWriteBytes(data[(offset + 0x118)..], Unknown3);
        BitConverter.TryWriteBytes(data[(offset + 0x11C)..], CountryAiBehavior);
        BitConverter.TryWriteBytes(data[(offset + 0x120)..], InitialTechLevel);
        BitConverter.TryWriteBytes(data[(offset + 0x124)..], Unknown4);
        BitConverter.TryWriteBytes(data[(offset + 0x128)..], InitialLaserGunLevel);
    }

    /// <summary>
    /// 创建默认军团
    /// </summary>
    public static Legion CreateDefault(int id) => new Legion
    {
        ActionId = id,
        CountryId = id,
        InitialEconomy = 1000,
        InitialIndustry = 100,
        InitialTech = 50,
        IsPlayerControlled = id == 1 ? 1 : 0,
        Camp = id,
        DefeatCondition = 0,
        CountryHpRate = 1.0f,
        CountryTaxRate = 0.1f,
        ColorR = 0xFF,
        ColorG = 0xFF,
        ColorB = 0xFF
    };

    public readonly double GetAverageArmyLevel()
    {
        int total = MobilityLevel + RifleLevel + CamouflageLevel + EngineerLevel +
                    GrenadeLevel + MortarLevel + MarchLevel + BulletproofVestLevel +
                    ArmorLevel + MainGunLevel + VehicleBodyLevel + EngineLevel +
                    MachineGunLevel + RaidLevel + VehicleAirDefenseLevel +
                    ReinforcedBodyLevel + ArtilleryLevel + RocketLevel + TowingLevel +
                    ArtilleryArmorLevel + FirepowerLevel;
        return total / 21.0;
    }

    public readonly double GetAverageNavyLevel()
    {
        int total = NavyRocketLevel + DisguiseLevel + HullLevel + PropulsionLevel +
                    NavyArmorLevel + WeaponLevel + NavalGunLevel + TorpedoLevel +
                    MinesweepingLevel + ShipAirDefenseLevel + ModernHullLevel;
        return total / 11.0;
    }

    public readonly double GetAverageAirForceLevel()
    {
        int total = AviationFuelLevel + AviationEngineLevel + AviationBombLevel +
                    AirRaidLevel + BombingLevel + StrategicBombingLevel +
                    AirdropLevel + JetEngineLevel;
        return total / 8.0;
    }

    public readonly double GetAverageDefenseLevel()
    {
        int total = BunkerLevel + FortressGunLevel + CoastalGunLevel +
                    RocketLauncherLevel + FortificationLevel + AntiAircraftGunLevel +
                    AntiAircraftCannonLevel + AntiAircraftMissileLevel + RadarLevel +
                    MissileWarheadLevel + SolidRocketEngineLevel +
                    NuclearBombBreakthroughLevel + NuclearFusionLevel;
        return total / 13.0;
    }
}
