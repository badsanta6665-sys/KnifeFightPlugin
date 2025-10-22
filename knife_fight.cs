using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json;

namespace KnifeFightPlugin;

[MinimumApiVersion(200)]
public class KnifeFightConfig
{
    public int KnifeFightDuration { get; set; } = 30;
    public bool EnableMusic { get; set; } = true;
    public int RewardMoney { get; set; } = 1000;
    public int MinPlayersForFight { get; set; } = 2;
    public bool EnableTeleport { get; set; } = true;
    public bool AnnounceMessages { get; set; } = true;
    public string MusicSound { get; set; } = "sounds/music/knife_fight.mp3";
    public string VictorySound { get; set; } = "sounds/music/victory.mp3";
}

public class KnifeFightPlugin : BasePlugin
{
    public override string ModuleName => "Knife Fight Plugin";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Your Name";
    public override string ModuleDescription => "Knife fight at the end of round with music";

    private KnifeFightConfig Config = new();
    private bool _isKnifeFightActive = false;
    private bool _isMusicPlaying = false;
    private Timer? _knifeFightTimer;

    public override void Load(bool hotReload)
    {
        LoadConfig();
        
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundAnnounceMatchStart>(OnRoundStart);
        
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        
        Console.WriteLine("[KnifeFight] Plugin loaded successfully!");
    }

    private void LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "config.json");
        
        if (!File.Exists(configPath))
        {
            SaveConfig();
            Console.WriteLine("[KnifeFight] Config file created, please edit it!");
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            Config = JsonSerializer.Deserialize<KnifeFightConfig>(json) ?? new KnifeFightConfig();
            Console.WriteLine($"[KnifeFight] Config loaded: Duration={Config.KnifeFightDuration}s, Reward=${Config.RewardMoney}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KnifeFight] Error loading config: {ex.Message}");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var configPath = Path.Combine(ModuleDirectory, "config.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(Config, options);
            File.WriteAllText(configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KnifeFight] Error saving config: {ex.Message}");
        }
    }

    private HookResult OnRoundStart(EventRoundStart eventObj, GameEventInfo info)
    {
        ResetKnifeFight();
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundAnnounceMatchStart eventObj, GameEventInfo info)
    {
        ResetKnifeFight();
        return HookResult.Continue;
    }

    private void ResetKnifeFight()
    {
        _isKnifeFightActive = false;
        _isMusicPlaying = false;
        
        _knifeFightTimer?.Kill();
        _knifeFightTimer = null;
    }

    private HookResult OnRoundEnd(EventRoundEnd eventObj, GameEventInfo info)
    {
        if (_isKnifeFightActive) return HookResult.Continue;

        var players = Utilities.GetPlayers();
        var alivePlayers = players.Where(p => 
            p != null && 
            p.IsValid && 
            p.PlayerPawn.IsValid && 
            p.PawnIsAlive && 
            p.TeamNum != (int)CsTeam.None && 
            p.TeamNum != (int)CsTeam.Spectator
        ).ToList();

        if (alivePlayers.Count == Config.MinPlayersForFight)
        {
            var team1Players = alivePlayers.Where(p => p.TeamNum == (int)CsTeam.CounterTerrorist).ToList();
            var team2Players = alivePlayers.Where(p => p.TeamNum == (int)CsTeam.Terrorist).ToList();

            if (team1Players.Count == 1 && team2Players.Count == 1)
            {
                Server.NextFrame(() =>
                {
                    StartKnifeFight(team1Players[0], team2Players[0]);
                });
            }
        }
        
        return HookResult.Continue;
    }

    private void StartKnifeFight(CCSPlayerController player1, CCSPlayerController player2)
    {
        if (player1 == null || !player1.IsValid || player2 == null || !player2.IsValid)
            return;

        _isKnifeFightActive = true;

        // Сообщения
        if (Config.AnnounceMessages)
        {
            Server.PrintToChatAll($" \x08[KNIFE FIGHT] \x01Двое последних игроков начинают ножевой бой!");
            Server.PrintToChatAll($" \x08[KNIFE FIGHT] \x01{player1.PlayerName} vs {player2.PlayerName}");
        }

        // Музыка
        if (Config.EnableMusic)
        {
            PlayKnifeFightMusic();
        }

        // Подготовка игроков
        PreparePlayerForFight(player1);
        PreparePlayerForFight(player2);

        // Телепортация
        if (Config.EnableTeleport)
        {
            TeleportPlayersClose(player1, player2);
        }

        // Таймер
        _knifeFightTimer = AddTimer(Config.KnifeFightDuration, () =>
        {
            if (_isKnifeFightActive)
            {
                EndKnifeFightTimeout();
            }
        });

        // Анонс
        AnnounceKnifeFightStart();
    }

    private void PreparePlayerForFight(CCSPlayerController player)
    {
        if (player?.PlayerPawn?.Value == null) return;

        // Забираем всё оружие
        StripAllWeapons(player);
        
        // Даём нож
        player.GiveNamedItem("weapon_knife");
        
        // Переключаем на нож
        player.ExecuteClientCommand("slot3");
    }

    private void StripAllWeapons(CCSPlayerController player)
    {
        if (player?.PlayerPawn?.Value?.WeaponServices == null) return;

        var weapons = player.PlayerPawn.Value.WeaponServices.MyWeapons;
        if (weapons == null) return;

        foreach (var weaponHandle in weapons)
        {
            if (weaponHandle?.IsValid != true || weaponHandle.Value?.IsValid != true)
                continue;

            var weapon = weaponHandle.Value;

            // Пропускаем ножи
            if (weapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
                weapon.DesignerName.Contains("bayonet", StringComparison.OrdinalIgnoreCase))
                continue;

            // Удаляем оружие
            player.PlayerPawn.Value.RemovePlayerItem(weapon);
            weapon.Remove();
        }
    }

    private void PlayKnifeFightMusic()
    {
        if (_isMusicPlaying) return;

        _isMusicPlaying = true;

        var players = Utilities.GetPlayers();
        foreach (var player in players.Where(p => p?.IsValid == true))
        {
            // Используем стандартные звуки CS2
            player.ExecuteClientCommand("play ui/cs2_ui_contract_complete");
        }

        if (Config.AnnounceMessages)
        {
            Server.PrintToChatAll($" \x08[KNIFE FIGHT] \x10♫ Музыка для боя включена!");
        }
    }

    private void StopMusic()
    {
        if (!_isMusicPlaying) return;

        _isMusicPlaying = false;

        var players = Utilities.GetPlayers();
        foreach (var player in players.Where(p => p?.IsValid == true))
        {
            player.ExecuteClientCommand("stopsound");
        }
    }

    private void PlayVictorySound()
    {
        if (!Config.EnableMusic) return;

        var players = Utilities.GetPlayers();
        foreach (var player in players.Where(p => p?.IsValid == true))
        {
            player.ExecuteClientCommand("play ui/cs2_ui_quest_complete");
        }
    }

    private void TeleportPlayersClose(CCSPlayerController player1, CCSPlayerController player2)
    {
        if (player1?.PlayerPawn?.Value == null || player2?.PlayerPawn?.Value == null)
            return;

        var pawn1 = player1.PlayerPawn.Value;
        var pawn2 = player2.PlayerPawn.Value;

        // Используем позицию первого игрока как основу
        var centerPos = pawn1.AbsOrigin ?? new Vector(0, 0, 0);

        // Телепортируем второго игрока на небольшом расстоянии
        var offsetPos = centerPos + new Vector(300, 0, 0);

        pawn2.Teleport(
            offsetPos,
            pawn2.EyeAngles ?? new QAngle(0, 0, 0),
            new Vector(0, 0, 0)
        );
    }

    private void AnnounceKnifeFightStart()
    {
        if (!Config.AnnounceMessages) return;

        Server.PrintToChatAll($" \x08[KNIFE FIGHT] \x06⚔️ НОЖЕВОЙ БОЙ НАЧАЛСЯ! ⚔️");
        Server.PrintToChatAll($" \x08[KNIFE FIGHT] \x01У вас есть {Config.KnifeFightDuration} секунд!");

        var players = Utilities.GetPlayers();
        foreach (var player in players.Where(p => p?.IsValid == true))
        {
            player.PrintToCenter("⚔️ НОЖЕВОЙ БОЙ! ⚔️\nСразитесь в честном поединке!");
        }
    }

    private HookResult OnPlayerDeath(EventPlayerDeath eventObj, GameEventInfo info)
    {
        if (!_isKnifeFightActive) return HookResult.Continue;

        var players = Utilities.GetPlayers();
        var alivePlayers = players.Where(p =>
            p?.IsValid == true &&
            p.PlayerPawn.IsValid &&
            p.PawnIsAlive &&
            p.TeamNum != (int)CsTeam.None &&
            p.TeamNum != (int)CsTeam.Spectator
        ).ToList();

        if (alivePlayers.Count == 1)
        {
            Server.NextFrame(() =>
            {
                EndKnifeFight(alivePlayers[0]);
            });
        }

        return HookResult.Continue;
    }

    private void EndKnifeFight(CCSPlayerController winner)
    {
        if (!_isKnifeFightActive) return;

        _isKnifeFightActive = false;
        StopMusic();
        PlayVictorySound();
        
        _knifeFightTimer?.Kill();
        _knifeFightTimer = null;

        // Награда победителю
        if (winner.InGameMoneyServices != null)
        {
            winner.InGameMoneyServices.Account += Config.RewardMoney;
        }

        // Сообщения
        if (Config.AnnounceMessages)
        {
            Server.PrintToChatAll($" \x08[KNIFE FIGHT] \x06🏆 ПОБЕДИТЕЛЬ: {winner.PlayerName} 🏆");
            Server.PrintToChatAll($" \x08[KNIFE FIGHT] \x01Ножевой бой завершён! +${Config.RewardMoney} награды");
        }

        var players = Utilities.GetPlayers();
        foreach (var player in players.Where(p => p?.IsValid == true))
        {
            if (player == winner)
            {
                player.PrintToCenter($"🏆 ВЫ ПОБЕДИЛИ В НОЖЕВОМ БОЮ! 🏆\n+${Config.RewardMoney} награды!");
            }
            else
            {
                player.PrintToCenter($"🏆 Победитель ножевого боя: {winner.PlayerName}");
            }
        }
    }

    private void EndKnifeFightTimeout()
    {
        if (!_isKnifeFightActive) return;

        _isKnifeFightActive = false;
        StopMusic();

        if (Config.AnnounceMessages)
        {
            Server.PrintToChatAll($" \x08[KNIFE FIGHT] \x01Время ножевого боя вышло! Ничья.");
        }

        // Завершаем бой ничьей - убиваем оставшихся игроков
        var players = Utilities.GetPlayers();
        var alivePlayers = players.Where(p =>
            p?.IsValid == true &&
            p.PlayerPawn.IsValid &&
            p.PawnIsAlive
        ).ToList();

        foreach (var player in alivePlayers)
        {
            player.CommitSuicide(false, true);
        }
    }

    private void OnMapStart(string mapName)
    {
        ResetKnifeFight();
        Console.WriteLine($"[KnifeFight] Map started: {mapName}");
    }

    public override void Unload(bool hotReload)
    {
        ResetKnifeFight();
        StopMusic();
        Console.WriteLine("[KnifeFight] Plugin unloaded");
    }
}
