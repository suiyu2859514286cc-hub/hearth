using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockLauncher;

/// <summary>推荐的服务器列表</summary>
public static class ServerCatalog
{
    public record Server(string Name, string Host, string Note, string Version, string Players, string Description);

    public static readonly List<Server> All = new()
    {
        new Server("Hypixel", "mc.hypixel.net", "小游戏", "1.8 - 1.21", "常年上万人在线",
            "全球最大的 Minecraft 服务器，几十种小游戏：BedWars、SkyWars、Skyblock、Murder Mystery 等等。\n\n" +
            "需要正版账号才能进。国内直连延迟比较高，建议用加速器。"),

        new Server("2b2t", "2b2t.org", "无政府", "1.12 / 1.20", "几十到几百人",
            "最老的无政府服务器，没有任何规则：可以炸、可以偷、可以杀。\n\n" +
            "进去要做好心理准备——出生点附近通常是一片废墟，而且经常有人在堵门。需要正版账号。"),

        new Server("Wynncraft", "play.wynncraft.com", "MMORPG", "1.12 - 1.21", "一两千人",
            "把 Minecraft 做成了大型多人在线 RPG：几十个职业、上千个任务、自己设计的地图和装备系统。\n\n" +
            "不用模组，直接连就能玩。需要正版账号。"),

        new Server("Mineplex", "us.mineplex.com", "小游戏", "1.8 - 1.20", "数百人",
            "老牌小游戏服务器，玩法跟 Hypixel 类似但更轻量，有 BedWars、SkyWars、Survival Games 等。\n\n" +
            "需要正版账号。"),

        new Server("CubeCraft", "play.cubecraft.net", "小游戏", "1.8 - 1.21", "几千人",
            "欧洲老牌小游戏服，BedWars、EggWars、SkyWars 都做得比较扎实，界面和音效很用心。\n\n" +
            "需要正版账号，国内延迟中等。"),

        new Server("The Hive", "play.hivemc.com", "小游戏", "1.8 - 1.21", "几千人",
            "另一个老牌小游戏服，有 BedWars、SkyWars、Murder Mystery、Build Battle 等，加载速度比较快。\n\n" +
            "需要正版账号。"),

        new Server("Purple Prison", "purpleprison.net", "监狱", "1.8 - 1.20", "数百人",
            "监狱玩法（Prison）代表服：挖矿、升等级、换装备，一层层往上爬，后面还有 PvP 区。\n\n" +
            "休闲向，不需要正版。"),

        new Server("Manacube", "play.manacube.com", "综合", "1.8 - 1.21", "数百人",
            "服务器群，里面有好几种玩法分区：生存、空岛、监狱、RPG，各有独立经济系统。\n\n" +
            "需要正版账号。"),

        new Server("PikaNetwork", "play.pika-network.net", "综合", "1.8 - 1.20", "几百到上千",
            "欧洲服，主打 BedWars、SkyWars、Practice（1v1 竞技），PvP 手感接近 Hypixel。\n\n" +
            "不需要正版，延迟较低。"),

        new Server("Origin Realms", "play.originrealms.com", "生存", "1.19 - 1.21", "几百人",
            "加了大量自定义内容和材质的生存服，有自己设计的地形、生物、任务线，像半个整合包。\n\n" +
            "需要正版账号，对配置要求略高。"),

        new Server("Skyblock.net", "mc.skyblock.net", "空岛", "1.8 - 1.20", "几十到几百人",
            "纯空岛服，从一棵树开始，靠刷怪、种田、交易慢慢做大，适合喜欢慢慢肝的人。\n\n" +
            "不需要正版。"),

        new Server("MCCentral", "play.mccentral.org", "综合", "1.8 - 1.21", "数百人",
            "综合服，生存、空岛、监狱、小游戏都有分区，人不多但氛围比较松。\n\n" +
            "不需要正版。"),
    };

    /// <summary>随机取一批（换一批用）</summary>
    public static List<Server> Pick(int count, Random rng)
    {
        return All.OrderBy(_ => rng.Next()).Take(count).ToList();
    }
}
