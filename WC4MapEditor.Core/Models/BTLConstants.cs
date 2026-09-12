namespace WC4MapEditor.Core.Models;

public enum BTLBuildingType { City = 0, CountyTown = 1, Port = 2, Pass = 3, Fortress = 4 }
public enum BTLKeyPointType { None = 0, RedCircle = 1, GreenCircle = 2 }
public enum BTLTerrainType { Sea = 0, Plains = 1, Forest = 2, Mountain = 3, Hill = 4, Desert = 5, Swamp = 6, Snow = 7 }
public enum BTLUnitType { Infantry = 0, Cavalry = 1, Archer = 2, Navy = 4, AirForce = 5 }
public enum BTLDirection { North = 0, Northeast = 1, East = 2, Southeast = 3, South = 4, Southwest = 5, West = 6, Northwest = 7 }
public enum BTLMilitaryRank { Soldier = 0, Corporal = 1, Sergeant = 2, Lieutenant = 3, Captain = 4, Major = 5, Colonel = 6, General = 7, Marshal = 8 }
public enum BTLNobilityRank { None = 0, Baron = 1, Viscount = 2, Earl = 3, Marquis = 4, Duke = 5, Prince = 6, King = 7, Emperor = 8 }
public enum BTLWeatherType { Clear = 0, Cloudy = 1, Rain = 2, SnowWeather = 3, Fog = 4, Storm = 5 }

[Flags]
public enum BTLRiverDirection { None = 0, North = 1, Northeast = 2, Southeast = 4, South = 8, Southwest = 16, Northwest = 32 }

public enum BTLFileType { Unknown = 0, Campaign = 1, Conquest = 2 }
