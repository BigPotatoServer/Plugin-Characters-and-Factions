using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using Newtonsoft.Json;
using MEC;
using PlayerRoles;
using CommandSystem;
using InventorySystem;
using PlayerStatsSystem;
using UnityEngine;
using MapGeneration;
using Random = UnityEngine.Random;
using Exiled.API.Enums;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.API.Features.Items;
using Exiled.API.Features.Pickups;
using Exiled.API.Features.Roles;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using Exiled.API.Features.DamageHandlers;
using CustomPlayerEffects;
using Exiled.API.Interfaces;
using Player = Exiled.API.Features.Player;
using Server = Exiled.API.Features.Server;
using Cassie = Exiled.API.Features.Cassie;
using RemoteAdmin;
using VoiceChat;
using Exiled.API.Features.Spawn;
using Environment = System.Environment;
using InventorySystem.Items.Usables;
using Effects = CustomPlayerEffects;
using Exiled.Events.EventArgs.Scp049;

namespace FactionPlugin
{
    public class FactionPlugin : Plugin<Config>
    {
        public static FactionPlugin Instance;
        public static Config PluginConfig;

        public override string Author => "大番茄 大土豆服务器";
        public override string Name => "cjjs";
        public override string Prefix => "cjjs";
        public override Version Version => new Version(1, 0, 0);
        public override Version RequiredExiledVersion => new Version(8, 2, 0);

        #region 单例和基础属性
        public static FactionPlugin Plugin { get; private set; }
        #endregion

        #region 颜色配置
        private readonly string _colorFoundation = "#00a8ff";
        private readonly string _colorChaos = "#ff9a00";
        private readonly string _colorScp = "#ff3f34";
        private readonly string _colorGoc = "#af7ac5";
        private readonly string _colorSerpents = "#f5b7b1";
        private readonly string _colorSpecial = "#f1c40f";
        private readonly string _colorDefault = "white";
        #endregion

        #region 特殊角色系统
        public static List<Player> Scp008Players { get; } = new List<Player>();
        public static List<Player> Scp181Players { get; } = new List<Player>();
        public static List<Player> Scp999Players { get; } = new List<Player>();
        public static List<Player> Scp682Players { get; } = new List<Player>();
        public static List<Player> Scp6821Players { get; } = new List<Player>();
        public static List<Player> Scp6822Players { get; } = new List<Player>();
        public static List<Player> Scp3114Players { get; } = new List<Player>();
        public static List<Player> Scp035Players { get; } = new List<Player>();
        public static List<ushort> Scp2818Items { get; } = new List<ushort>();
        public static List<ushort> Scp035Items { get; } = new List<ushort>();
        private static bool _scp999FlashlightActive;
        private int _scp035ReviveCount = 0;
        #endregion

        #region 阵营系统
        public enum CustomFaction
        {
            None,
            Foundation,
            ChaosInsurgency,
            SCP,
            SerpentsHand,
            GOC
        }

        private CustomFaction GetPlayerFaction(Player player)
        {
            if (player == null || !player.IsConnected) return CustomFaction.None;

            if (player.CustomInfo == "GOC收容部队") return CustomFaction.GOC;
            if (player.CustomInfo == "蛇之手") return CustomFaction.SerpentsHand;
            if (Scp035Players.Contains(player)) return CustomFaction.SCP;

            switch (player.Role.Team)
            {
                case Team.FoundationForces:
                case Team.Scientists:
                    return CustomFaction.Foundation;
                case Team.ChaosInsurgency:
                case Team.ClassD:
                    return CustomFaction.ChaosInsurgency;
                case Team.SCPs:
                    return CustomFaction.SCP;
                default:
                    return CustomFaction.None;
            }
        }

        #region 新增特殊阵营
        public static List<Player> ChaosFastResponsePlayers { get; } = new List<Player>();
        public static List<Player> FoundationFastResponsePlayers { get; } = new List<Player>();
        public static List<Player> GOCCapturePlayers { get; } = new List<Player>();
        public static List<Player> SerpentsHandPlayers { get; } = new List<Player>();
        public static List<Player> DeltaLegionPlayers { get; } = new List<Player>();
        public static List<Player> AlphaNinePlayers { get; } = new List<Player>();

        private Dictionary<Player, CoroutineHandle> _playerHintCoroutines = new Dictionary<Player, CoroutineHandle>();

        private bool _chaosSpawned = false;
        private bool _foundationSpawned = false;
        private bool _gocSpawned = false;
        private bool _serpentsHandSpawned = false;
        private bool _deltaLegionSpawned = false;
        private bool _alphaNineSpawned = false;
        #endregion
        #endregion

        #region 友军伤害重试系统
        private CoroutineHandle _friendlyFireRetryCoroutine;
        private bool _isFriendlyFireSet = false;

        private IEnumerator<float> FriendlyFireRetryCoroutine()
        {
            int retryCount = 0;
            while (!_isFriendlyFireSet && retryCount < 10)
            {
                bool success = false;
                try
                {
                    Server.FriendlyFire = PluginConfig.EnableFriendlyFire;
                    _isFriendlyFireSet = true;
                    success = true;

                    if (PluginConfig.Debug)
                        Log.Debug($"友军伤害设置成功: {PluginConfig.EnableFriendlyFire}");
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Log.Warn($"设置友军伤害失败 ({retryCount}/10): {ex.Message}");
                }

                if (!success)
                {
                    // 等待5秒后重试
                    yield return Timing.WaitForSeconds(5f);
                }
            }

            if (!_isFriendlyFireSet)
            {
                Log.Error("友军伤害设置失败，已达到最大重试次数");
            }
        }

        private void StartFriendlyFireRetry()
        {
            _isFriendlyFireSet = false;

            if (_friendlyFireRetryCoroutine.IsRunning)
                Timing.KillCoroutines(_friendlyFireRetryCoroutine);

            _friendlyFireRetryCoroutine = Timing.RunCoroutine(FriendlyFireRetryCoroutine());
        }
        #endregion

        #region 持续提示系统
        private string GetPlayerRoleHintString(Player player)
        {
            if (player == null || !player.IsConnected)
                return string.Empty;

            if (player.Role.Type == RoleTypeId.Spectator)
            {
                return $"<color={_colorDefault}><b>🕵️ 你正在扮演: 观察者</b></color>\n<color=#AAAAAA>使用鼠标滚轮切换视角，Tab键改装武器</color>";
            }

            string roleName;
            string colorHex;
            string factionIcon;
            string additionalInfo = "";

            if (Scp008Players.Contains(player))
            {
                roleName = "🦠 SCP-008";
                colorHex = _colorScp;
                factionIcon = GetFactionIcon(CustomFaction.SCP);
                additionalInfo = "\n<color=#FF6B6B>🦠 你的攻击会感染其他玩家</color>";
            }
            else if (Scp181Players.Contains(player))
            {
                roleName = "🎲 SCP-181";
                colorHex = _colorSpecial;
                factionIcon = GetFactionIcon(CustomFaction.None);
                additionalInfo = "\n<color=#FFD700>🎲 你有概率免疫伤害并直接开启门禁</color>";
            }
            else if (Scp999Players.Contains(player))
            {
                roleName = "💖 SCP-999";
                colorHex = _colorScp;
                factionIcon = GetFactionIcon(CustomFaction.SCP);
                additionalInfo = "\n<color=#FFD700>✨ 你的攻击会治疗敌人，手电筒可为队友回血</color>";
            }
            else if (Scp682Players.Contains(player))
            {
                roleName = "🐲 SCP-682 (第一形态)";
                colorHex = _colorScp;
                factionIcon = GetFactionIcon(CustomFaction.SCP);
                additionalInfo = "\n<color=#FF6B6B>💀 你拥有2次复活机会</color>";
            }
            else if (Scp6821Players.Contains(player))
            {
                roleName = "🐉 SCP-682-1 (第二形态)";
                colorHex = _colorScp;
                factionIcon = GetFactionIcon(CustomFaction.SCP);
                additionalInfo = "\n<color=#FF6B6B>💀 你拥有1次复活机会</color>";
            }
            else if (Scp6822Players.Contains(player))
            {
                roleName = "☠️ SCP-682-2 (最终形态)";
                colorHex = _colorScp;
                factionIcon = GetFactionIcon(CustomFaction.SCP);
                additionalInfo = "\n<color=#FF6B6B>💀 这是你的最后形态</color>";
            }
            else if (Scp3114Players.Contains(player))
            {
                roleName = "👥 SCP-3114";
                colorHex = _colorScp;
                factionIcon = GetFactionIcon(CustomFaction.SCP);
                additionalInfo = "\n<color=#FFD700>🎭 你是人形终结者</color>";
            }
            else if (Scp035Players.Contains(player))
            {
                roleName = "🎭 SCP-035";
                colorHex = _colorScp;
                factionIcon = GetFactionIcon(CustomFaction.SCP);
                additionalInfo = "\n<color=#FFD700>💀 你属于SCP阵营</color>";
            }
            else if (ChaosFastResponsePlayers.Contains(player))
            {
                roleName = "⚔️ 混沌快速支援部队";
                colorHex = _colorChaos;
                factionIcon = GetFactionIcon(CustomFaction.ChaosInsurgency);
                additionalInfo = "\n<color=#FFA500>💥 消灭所有敌人</color>";
            }
            else if (FoundationFastResponsePlayers.Contains(player))
            {
                roleName = "🛡️ 基金会快速支援部队";
                colorHex = _colorFoundation;
                factionIcon = GetFactionIcon(CustomFaction.Foundation);
                additionalInfo = "\n<color=#00a8ff>🔒 保护设施安全</color>";
            }
            else if (GOCCapturePlayers.Contains(player))
            {
                roleName = "🟣 GOC收容部队";
                colorHex = _colorGoc;
                factionIcon = GetFactionIcon(CustomFaction.GOC);
                additionalInfo = "\n<color=#af7ac5>🎯 收容SCP和消灭蛇之手</color>";
            }
            else if (SerpentsHandPlayers.Contains(player))
            {
                roleName = "🐍 蛇之手";
                colorHex = _colorSerpents;
                factionIcon = GetFactionIcon(CustomFaction.SerpentsHand);
                additionalInfo = "\n<color=#f5b7b1>🤝 帮助SCP，消灭所有人类</color>";
            }
            else if (DeltaLegionPlayers.Contains(player))
            {
                roleName = "💀 德尔塔军团";
                colorHex = _colorChaos;
                factionIcon = GetFactionIcon(CustomFaction.ChaosInsurgency);
                additionalInfo = "\n<color=#FFA500>⚡ 精英混沌部队</color>";
            }
            else if (AlphaNinePlayers.Contains(player))
            {
                roleName = "👑 Alpha-9最后的希望";
                colorHex = _colorFoundation;
                factionIcon = GetFactionIcon(CustomFaction.Foundation);
                additionalInfo = "\n<color=#00a8ff>🌟 基金会最后的希望</color>";
            }
            else
            {
                roleName = player.Role.Type.ToString();
                CustomFaction faction = GetPlayerFaction(player);
                factionIcon = GetFactionIcon(faction);
                colorHex = GetFactionColor(faction);

                switch (roleName)
                {
                    case "ClassD":
                        roleName = "🔓 D级人员";
                        additionalInfo = "\n<color=#FFA500>🏃‍♂️ 逃离设施或加入混沌</color>";
                        break;
                    case "Scientist":
                        roleName = "🔬 科学家";
                        additionalInfo = "\n<color=#00a8ff>🎯 逃离设施或等待救援</color>";
                        break;
                    case "FacilityGuard":
                        roleName = "🛡️ 设施警卫";
                        additionalInfo = "\n<color=#00a8ff>🔫 保护科学家，消灭威胁</color>";
                        break;
                    case "NtfPrivate":
                        roleName = "🎖️ 九尾狐新兵";
                        additionalInfo = "\n<color=#00a8ff>🔒 收容SCP，保护设施</color>";
                        break;
                    case "NtfSergeant":
                        roleName = "💎 九尾狐士官";
                        additionalInfo = "\n<color=#00a8ff>🔒 收容SCP，保护设施</color>";
                        break;
                    case "NtfSpecialist":
                        roleName = "⚡ 九尾狐专家";
                        additionalInfo = "\n<color=#00a8ff>🔒 收容SCP，保护设施</color>";
                        break;
                    case "NtfCaptain":
                        roleName = "👑 九尾狐上尉";
                        additionalInfo = "\n<color=#00a8ff>🔒 收容SCP，保护设施</color>";
                        break;
                    case "ChaosRifleman":
                        roleName = "⚔️ 混沌分裂者";
                        additionalInfo = "\n<color=#FFA500>💥 消灭基金会，拯救D级</color>";
                        break;
                    case "ChaosRepressor":
                        roleName = "💀 混沌镇压者";
                        additionalInfo = "\n<color=#FFA500>💥 消灭基金会，拯救D级</color>";
                        break;
                    case "Scp049":
                        roleName = "🩺 SCP-049";
                        additionalInfo = "\n<color=#ff3f34>💀 将死者转化为你的奴仆</color>";
                        break;
                    case "Scp0492":
                        roleName = "🧟 SCP-049-2";
                        additionalInfo = "\n<color=#ff3f34>🧟 听从SCP-049的指挥</color>";
                        break;
                    case "Scp079":
                        roleName = "💻 SCP-079";
                        additionalInfo = "\n<color=#ff3f34>🔌 控制设施，协助其他SCP</color>";
                        break;
                    case "Scp096":
                        roleName = "😢 SCP-096";
                        additionalInfo = "\n<color=#ff3f34>😠 不要看你的脸</color>";
                        break;
                    case "Scp106":
                        roleName = "👴 SCP-106";
                        additionalInfo = "\n<color=#ff3f34>🕳️ 将猎物拖入口袋维度</color>";
                        break;
                    case "Scp173":
                        roleName = "🥜 SCP-173";
                        additionalInfo = "\n<color=#ff3f34>⚡ 在无人注视时移动</color>";
                        break;
                    case "Scp939":
                        roleName = "🐕 SCP-939";
                        additionalInfo = "\n<color=#ff3f34>🎤 模仿人类声音引诱猎物</color>";
                        break;
                    case "Tutorial":
                        roleName = "❓ 教程角色";
                        additionalInfo = "\n<color=#FFFFFF>📚 学习游戏机制</color>";
                        break;
                    default:
                        roleName = $"❓ {roleName}";
                        additionalInfo = "\n<color=#FFFFFF>🔍 探索你的能力</color>";
                        break;
                }
            }

            if (!player.IsAlive)
            {
                additionalInfo = $"\n<color=#FF6B6B>☠️ 你已阵亡，正在观察中...</color>";
            }

            return $"<color={colorHex}><b>{factionIcon} 你正在扮演: {roleName}</b></color>{additionalInfo}";
        }

        private string GetFactionIcon(CustomFaction faction)
        {
            switch (faction)
            {
                case CustomFaction.Foundation: return "🏢";
                case CustomFaction.ChaosInsurgency: return "☣️";
                case CustomFaction.SCP: return "🔴";
                case CustomFaction.GOC: return "🟣";
                case CustomFaction.SerpentsHand: return "🐍";
                default: return "⚪";
            }
        }

        private string GetFactionColor(CustomFaction faction)
        {
            switch (faction)
            {
                case CustomFaction.Foundation: return _colorFoundation;
                case CustomFaction.ChaosInsurgency: return _colorChaos;
                case CustomFaction.SCP: return _colorScp;
                case CustomFaction.GOC: return _colorGoc;
                case CustomFaction.SerpentsHand: return _colorSerpents;
                default: return _colorDefault;
            }
        }

        private string GetFormattedHint(string content)
        {
            string vertical = new string('\n', PluginConfig.HintVerticalOffset);
            string horizontal = new string(' ', PluginConfig.HintHorizontalOffset);
            return $"{vertical}{horizontal}<size={PluginConfig.HintFontSize}%>{content}</size>";
        }

        private void StartPersistentHint(Player player)
        {
            if (player == null || !player.IsConnected) return;

            if (_playerHintCoroutines.TryGetValue(player, out CoroutineHandle existingHandle))
            {
                Timing.KillCoroutines(existingHandle);
                _playerHintCoroutines.Remove(player);
            }

            var newHandle = Timing.RunCoroutine(RunPersistentHint(player));
            _playerHintCoroutines[player] = newHandle;
        }

        private void StopPersistentHint(Player player)
        {
            if (_playerHintCoroutines.TryGetValue(player, out CoroutineHandle handle))
            {
                Timing.KillCoroutines(handle);
                _playerHintCoroutines.Remove(player);
                player.ShowHint("", 0.1f);
            }
        }

        private IEnumerator<float> RunPersistentHint(Player player)
        {
            while (player != null && player.IsConnected)
            {
                if (!PluginConfig.EnableRoleHints)
                {
                    yield return Timing.WaitForSeconds(PluginConfig.HintRefreshInterval);
                    continue;
                }

                string hintText = GetPlayerRoleHintString(player);
                player.ShowHint(GetFormattedHint(hintText), PluginConfig.HintDuration);
                yield return Timing.WaitForSeconds(PluginConfig.HintRefreshInterval);
            }

            if (player != null)
            {
                _playerHintCoroutines.Remove(player);
            }
        }
        #endregion

        #region 属性重置系统
        private void ResetPlayerAttributes(Player player)
        {
            if (player == null || !player.IsConnected) return;

            try
            {
                player.Scale = Vector3.one;
                player.DisableEffect<Effects.Scp207>();
                player.DisableEffect<Effects.Scp1853>();
                player.DisableEffect<Effects.Invisible>();
                player.DisableEffect<Effects.BodyshotReduction>();
                player.DisableEffect<Effects.DamageReduction>();
                player.DisableEffect<Effects.MovementBoost>();

                Scp008Players.Remove(player);
                Scp181Players.Remove(player);
                Scp999Players.Remove(player);
                Scp3114Players.Remove(player);
                Scp035Players.Remove(player);

                ChaosFastResponsePlayers.Remove(player);
                FoundationFastResponsePlayers.Remove(player);
                GOCCapturePlayers.Remove(player);
                SerpentsHandPlayers.Remove(player);
                DeltaLegionPlayers.Remove(player);
                AlphaNinePlayers.Remove(player);

                player.CustomInfo = string.Empty;
                StopPersistentHint(player);

                if (PluginConfig.Debug)
                {
                    Log.Debug($"已重置玩家 {player.Nickname} 的属性");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"重置玩家属性失败: {ex}");
            }
        }
        #endregion

        #region 特殊角色系统
        public void SpawnScp008(Player player)
        {
            if (!PluginConfig.EnableScp008) return;

            ResetPlayerAttributes(player);

            player.Role.Set(RoleTypeId.Scp0492);
            player.MaxHealth = 2500;
            player.Health = 2500;
            Scp008Players.Add(player);

            player.ShowHint(GetFormattedHint($"<color={_colorScp}>你已成为SCP-008</color> - 获得三重极速移动能力"), 5f);

            player.Teleport(RoomType.Hcz939);

            for (int i = 0; i < 3; i++)
            {
                player.EnableEffect<Effects.Scp207>(9999f);
            }

            player.ChangeEffectIntensity<Effects.Scp207>(3);
            StartPersistentHint(player);
        }

        public void SpawnScp181(Player player)
        {
            if (!PluginConfig.EnableScp181) return;

            ResetPlayerAttributes(player);

            Scp181Players.Add(player);
            player.MaxHealth = 150;
            player.Health = 150;

            player.ShowHint(GetFormattedHint($"<color={_colorSpecial}>你已成为SCP-181</color> - 概率免疫伤害，可随机开启门禁"), 5f);
            StartPersistentHint(player);
        }

        public void SpawnScp999(Player player)
        {
            if (!PluginConfig.EnableScp999) return;

            ResetPlayerAttributes(player);

            player.Role.Set(RoleTypeId.Tutorial);
            player.MaxHealth = PluginConfig.Scp999Health;
            player.Health = PluginConfig.Scp999Health;
            player.Scale = new Vector3(PluginConfig.Scp999Scale, PluginConfig.Scp999Scale, PluginConfig.Scp999Scale);
            player.AddItem(ItemType.GunFRMG0);
            player.AddItem(ItemType.Flashlight);
            player.AddItem(ItemType.ArmorHeavy);
            player.AddItem(ItemType.KeycardO5);
            Scp999Players.Add(player);

            player.ShowHint(GetFormattedHint($"<color={_colorScp}>你已成为SCP-999</color> - 手持手电筒可为周围玩家回血\n你的攻击会治疗敌人"), 5f);

            player.Teleport(RoomType.LczArmory);
            player.EnableEffect<Effects.Scp207>();
            StartPersistentHint(player);
        }

        public void SpawnScp3114(Player player)
        {
            if (!PluginConfig.EnableScp3114) return;

            ResetPlayerAttributes(player);

            player.Role.Set(RoleTypeId.Scp3114);
            Scp3114Players.Add(player);

            player.ShowHint(GetFormattedHint($"<color={_colorScp}>你已成为SCP-3114</color> - 人形终结者"), 5f);
            StartPersistentHint(player);
        }

        public void SpawnScp035(Player player)
        {
            if (!PluginConfig.EnableScp035) return;

            ResetPlayerAttributes(player);

            List<ItemType> originalItems = new List<ItemType>();
            foreach (Item item in player.Items)
            {
                originalItems.Add(item.Type);
            }

            Item scp035Item = null;
            foreach (Item item in player.Items)
            {
                if (Scp035Items.Contains(item.Serial))
                {
                    scp035Item = item;
                    break;
                }
            }

            if (scp035Item != null)
            {
                player.RemoveItem(scp035Item);
            }

            Scp035Players.Add(player);
            player.Role.Set(RoleTypeId.Tutorial);
            player.MaxHealth = 600;
            player.Health = 600;
            player.MaxHumeShield = 400;
            player.HumeShield = 400;

            foreach (var itemType in originalItems)
            {
                if (itemType != ItemType.SCP268)
                {
                    player.AddItem(itemType);
                }
            }

            player.ShowHint(GetFormattedHint($"<color={_colorScp}>◆你是SCP-035◆</color>\n<color=#FFFF00>scp阵营</color>"), 5f);
            StartPersistentHint(player);
        }

        public void SpawnScp682(Player player)
        {
            if (!PluginConfig.EnableScp682) return;

            ResetPlayerAttributes(player);

            Scp682Players.Add(player);
            player.Role.Set(RoleTypeId.Scp939);
            player.MaxHealth = 3000;
            player.Health = 3000;
            player.MaxHumeShield = 400;
            player.HumeShield = 400;

            player.ShowHint(GetFormattedHint($"<color={_colorScp}>◆ 你是SCP-682，可以复活<color=#00FF00>2</color>次◆</color>"), 5f);
            StartPersistentHint(player);
        }

        public void SpawnScp6821(Player player)
        {
            if (!PluginConfig.EnableScp682) return;

            ResetPlayerAttributes(player);

            Scp6821Players.Add(player);
            player.Role.Set(RoleTypeId.Scp939);
            player.MaxHealth = 1500;
            player.Health = 600;
            player.MaxHumeShield = 200;
            player.HumeShield = 100;

            player.ShowHint(GetFormattedHint($"<color={_colorScp}>◆ 你是SCP-682-1，可以复活<color=#00FF00>1</color>次◆</color>"), 5f);

            player.Scale = new Vector3(0.5f, 0.5f, 0.5f);
            Timing.RunCoroutine(RebirthCoroutine(player));
            StartPersistentHint(player);
        }

        public void SpawnScp6822(Player player)
        {
            if (!PluginConfig.EnableScp682) return;

            ResetPlayerAttributes(player);

            Scp6822Players.Add(player);
            player.Role.Set(RoleTypeId.Scp939);
            player.MaxHealth = 1000;
            player.Health = 300;
            player.MaxHumeShield = 200;
            player.HumeShield = 100;
            player.Scale = new Vector3(0.5f, 0.5f, 0.5f);

            player.ShowHint(GetFormattedHint($"<color={_colorScp}>◆ 你是SCP-682-2，<color=#00FF00>无法复活</color>◆</color>"), 5f);

            Timing.RunCoroutine(RebirthCoroutine2(player));
            StartPersistentHint(player);
        }

        private static IEnumerator<float> RebirthCoroutine(Player player)
        {
            for (int i = 0; i < 70; i += 5)
            {
                yield return Timing.WaitForSeconds(5f);
                if (player == null || !player.IsAlive)
                    yield break;

                player.Heal(75f);
                player.Scale = new Vector3(
                    Mathf.Clamp(player.Scale.x + 0.042f, 0f, 1f),
                    Mathf.Clamp(player.Scale.y + 0.042f, 0f, 1f),
                    Mathf.Clamp(player.Scale.z + 0.042f, 0f, 1f)
                );
            }

            if (player != null && player.IsAlive)
            {
                player.HumeShield = 200;
            }
        }

        private static IEnumerator<float> RebirthCoroutine2(Player player)
        {
            for (int i = 0; i < 70; i += 5)
            {
                yield return Timing.WaitForSeconds(5f);
                if (player == null || !player.IsAlive)
                    yield break;

                player.Heal(58.33f);
                player.Scale = new Vector3(
                    Mathf.Clamp(player.Scale.x + 0.042f, 0f, 1f),
                    Mathf.Clamp(player.Scale.y + 0.042f, 0f, 1f),
                    Mathf.Clamp(player.Scale.z + 0.042f, 0f, 1f)
                );
            }

            if (player != null && player.IsAlive)
            {
                player.HumeShield = 200;
            }
        }
        #endregion

        #region 新增特殊阵营生成方法
        private void SpawnChaosFastResponse()
        {
            if (!PluginConfig.EnableChaosFastResponse) return;
            if (Warhead.IsDetonated || Round.IsEnded || !PluginConfig.EnableSpecialTeamRespawn)
                return;

            var deadPlayers = Player.List.Where(p => p.IsDead).ToList();
            if (deadPlayers.Count == 0)
                return;

            int spawnCount = Math.Min(5, deadPlayers.Count);
            var selectedPlayers = deadPlayers.OrderBy(x => Guid.NewGuid()).Take(spawnCount).ToList();

            foreach (var player in selectedPlayers)
            {
                if (!player.IsConnected) continue;

                ResetPlayerAttributes(player);
                player.Role.Set(RoleTypeId.ChaosRifleman);
                player.ClearInventory();
                player.AddItem(ItemType.GunFRMG0);
                player.AddItem(ItemType.ArmorHeavy);
                player.AddItem(ItemType.Medkit);
                player.AddItem(ItemType.GrenadeHE);
                player.AddItem(ItemType.KeycardChaosInsurgency);
                ChaosFastResponsePlayers.Add(player);

                player.ShowHint(GetFormattedHint($"<color={_colorChaos}>你是混沌快速支援部队，消灭所有敌人</color>"), 10f);
                StartPersistentHint(player);
            }

            Cassie.MessageTranslated("warning chaos fast response force has entered the facility", "警告 混沌快速支援部队已进入设施");
        }

        private void SpawnFoundationFastResponse()
        {
            if (!PluginConfig.EnableFoundationFastResponse) return;
            if (Warhead.IsDetonated || Round.IsEnded || !PluginConfig.EnableSpecialTeamRespawn)
                return;

            var deadPlayers = Player.List.Where(p => p.IsDead).ToList();
            if (deadPlayers.Count == 0)
                return;

            int spawnCount = Math.Min(5, deadPlayers.Count);
            var selectedPlayers = deadPlayers.OrderBy(x => Guid.NewGuid()).Take(spawnCount).ToList();

            foreach (var player in selectedPlayers)
            {
                if (!player.IsConnected) continue;

                ResetPlayerAttributes(player);
                player.Role.Set(RoleTypeId.NtfSergeant);
                player.ClearInventory();
                player.AddItem(ItemType.GunFRMG0);
                player.AddItem(ItemType.ArmorHeavy);
                player.AddItem(ItemType.Medkit);
                player.AddItem(ItemType.GrenadeHE);
                player.AddItem(ItemType.KeycardChaosInsurgency);
                FoundationFastResponsePlayers.Add(player);

                player.ShowHint(GetFormattedHint($"<color={_colorFoundation}>你是基金会快速支援部队，消灭所有敌人</color>"), 10f);
                StartPersistentHint(player);
            }

            Cassie.MessageTranslated("foundation fast response force has entered the facility", "基金会快速支援部队已进入设施");
        }

        private void SpawnGOCCaptureTeam()
        {
            if (!PluginConfig.EnableGOCCapture) return;
            if (Warhead.IsDetonated || Round.IsEnded || !PluginConfig.EnableSpecialTeamRespawn) return;

            var deadPlayers = Player.List.Where(p => p.IsDead).ToList();
            if (deadPlayers.Count < 4) return;

            int spawnCount = Math.Min(4, deadPlayers.Count);
            var selectedPlayers = deadPlayers.OrderBy(x => Guid.NewGuid()).Take(spawnCount).ToList();

            foreach (var player in selectedPlayers)
            {
                if (!player.IsConnected) continue;

                ResetPlayerAttributes(player);
                player.Role.Set(RoleTypeId.Tutorial);
                player.ClearInventory();
                player.AddItem(ItemType.GunFRMG0);
                player.AddItem(ItemType.Jailbird);
                player.AddItem(ItemType.ArmorHeavy);
                player.AddItem(ItemType.GunCom45);
                player.AddItem(ItemType.SCP1853);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.KeycardO5);
                player.Teleport(RoomType.EzGateB);
                player.CustomInfo = "GOC收容部队";
                GOCCapturePlayers.Add(player);

                player.ShowHint(GetFormattedHint($"<color={_colorGoc}>你是GOC收容部队，收容SCP和消灭蛇之手</color>"), 15f);
                StartPersistentHint(player);
            }

            Cassie.MessageTranslated("g o c capture team has entered the facility", "GOC收容部队已进入设施");
        }

        private void SpawnSerpentsHand()
        {
            if (!PluginConfig.EnableSerpentsHand) return;
            if (Warhead.IsDetonated || Round.IsEnded || !PluginConfig.EnableSpecialTeamRespawn) return;

            var deadPlayers = Player.List.Where(p => p.IsDead).ToList();
            if (deadPlayers.Count < 4) return;

            int spawnCount = Math.Min(4, deadPlayers.Count);
            var selectedPlayers = deadPlayers.OrderBy(x => Guid.NewGuid()).Take(spawnCount).ToList();

            foreach (var player in selectedPlayers)
            {
                if (!player.IsConnected) continue;

                ResetPlayerAttributes(player);
                player.Role.Set(RoleTypeId.Tutorial);
                player.ClearInventory();
                player.AddItem(ItemType.GunFRMG0);
                player.AddItem(ItemType.Jailbird);
                player.AddItem(ItemType.ArmorHeavy);
                player.AddItem(ItemType.GunCom45);
                player.AddItem(ItemType.SCP1853);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.KeycardO5);
                player.Teleport(RoomType.Hcz096);
                player.CustomInfo = "蛇之手";
                SerpentsHandPlayers.Add(player);

                player.ShowHint(GetFormattedHint($"<color={_colorSerpents}>你是蛇之手，帮助SCP，消灭所有人类</color>"), 15f);
                StartPersistentHint(player);
            }

            Cassie.MessageTranslated("warning serpent hand has entered the facility", "警告 蛇之手已进入设施");
        }

        private void SpawnDeltaLegion()
        {
            if (!PluginConfig.EnableDeltaLegion) return;
            if (Warhead.IsDetonated || Round.IsEnded || !PluginConfig.EnableSpecialTeamRespawn) return;

            var deadPlayers = Player.List.Where(p => p.IsDead).ToList();
            if (deadPlayers.Count < 5) return;

            int spawnCount = Math.Min(5, deadPlayers.Count);
            var selectedPlayers = deadPlayers.OrderBy(x => Guid.NewGuid()).Take(spawnCount).ToList();

            foreach (var player in selectedPlayers)
            {
                if (!player.IsConnected) continue;

                ResetPlayerAttributes(player);
                player.Role.Set(RoleTypeId.ChaosRepressor);
                player.ClearInventory();
                player.AddItem(ItemType.GunFRMG0);
                player.AddItem(ItemType.MicroHID);
                player.AddItem(ItemType.ArmorHeavy);
                player.AddItem(ItemType.GunCom45);
                player.AddItem(ItemType.SCP1853);
                player.AddItem(ItemType.KeycardO5);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.SCP207);
                DeltaLegionPlayers.Add(player);

                player.ShowHint(GetFormattedHint($"<color={_colorChaos}>你是德尔塔军团，消灭所有敌人</color>"), 15f);
                StartPersistentHint(player);
            }

            Cassie.MessageTranslated("facility maximum alert delta legion has entered the facility", "设施最高警告 德尔塔军团已进入设施");
        }

        private void SpawnAlphaNine()
        {
            if (!PluginConfig.EnableAlphaNine) return;
            if (Round.IsEnded) return;

            var deadPlayers = Player.List.Where(p => p.IsDead).ToList();
            int spawnCount = Math.Min(5, deadPlayers.Count);
            var selectedPlayers = deadPlayers.OrderBy(x => Guid.NewGuid()).Take(spawnCount).ToList();

            foreach (var player in selectedPlayers)
            {
                if (!player.IsConnected) continue;

                ResetPlayerAttributes(player);
                player.Role.Set(RoleTypeId.NtfCaptain);
                player.ClearInventory();
                player.AddItem(ItemType.GunFRMG0);
                player.AddItem(ItemType.MicroHID);
                player.AddItem(ItemType.ArmorHeavy);
                player.AddItem(ItemType.GunCom45);
                player.AddItem(ItemType.SCP1853);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.SCP207);
                player.AddItem(ItemType.KeycardO5);
                AlphaNinePlayers.Add(player);

                player.ShowHint(GetFormattedHint($"<color={_colorFoundation}>你是Alpha-9最后的希望，消灭所有敌人</color>"), 15f);
                StartPersistentHint(player);
            }

            Cassie.MessageTranslated("mobile task force alpha nine last hope has entered the facility", "机动特遣队Alpha-9最后的希望已进入设施");
        }
        #endregion

        #region 新增阵营刷新协程
        private CoroutineHandle _scheduledSpawnsCoroutine;

        private IEnumerator<float> ScheduledSpawnsCoroutine()
        {
            yield return Timing.WaitForSeconds(150f);
            if (!Warhead.IsDetonated && Round.IsStarted && !Round.IsEnded && PluginConfig.EnableFoundationFastResponse)
            {
                SpawnFoundationFastResponse();
            }

            yield return Timing.WaitForSeconds(150f);
            if (!Warhead.IsDetonated && Round.IsStarted && !Round.IsEnded && PluginConfig.EnableChaosFastResponse)
            {
                SpawnChaosFastResponse();
            }

            yield return Timing.WaitForSeconds(150f);
            if (!Warhead.IsDetonated && Round.IsStarted && !Round.IsEnded && PluginConfig.EnableFoundationFastResponse)
            {
                SpawnFoundationFastResponse();
            }

            yield return Timing.WaitForSeconds(150f);
            if (!Warhead.IsDetonated && Round.IsStarted && !Round.IsEnded)
            {
                if (PluginConfig.EnableGOCCapture)
                    SpawnGOCCaptureTeam();

                if (PluginConfig.EnableChaosFastResponse)
                    SpawnChaosFastResponse();
            }

            yield return Timing.WaitForSeconds(150f);
            if (!Warhead.IsDetonated && Round.IsStarted && !Round.IsEnded && PluginConfig.EnableFoundationFastResponse)
            {
                SpawnFoundationFastResponse();
            }

            yield return Timing.WaitForSeconds(150f);
            if (!Warhead.IsDetonated && Round.IsStarted && !Round.IsEnded && PluginConfig.EnableChaosFastResponse)
            {
                SpawnChaosFastResponse();
            }

            yield return Timing.WaitForSeconds(150f);
            if (!Warhead.IsDetonated && Round.IsStarted && !Round.IsEnded && PluginConfig.EnableFoundationFastResponse)
            {
                SpawnFoundationFastResponse();
            }

            yield return Timing.WaitForSeconds(150f);
            if (!Warhead.IsDetonated && Round.IsStarted && !Round.IsEnded && PluginConfig.EnableChaosFastResponse)
            {
                SpawnChaosFastResponse();
            }
        }

        private IEnumerator<float> GOCCaptureSpawnCoroutine()
        {
            yield return Timing.WaitForSeconds(600f);

            if (!PluginConfig.EnableGOCCapture) yield break;
            if (Warhead.IsDetonated || Round.IsEnded || !Round.IsStarted || !PluginConfig.EnableSpecialTeamRespawn)
                yield break;

            while (Player.List.Count(p => p.IsDead) < 4 && !Round.IsEnded && !Warhead.IsDetonated && Round.IsStarted)
            {
                yield return Timing.WaitForSeconds(10f);
            }

            if (!Round.IsEnded && !Warhead.IsDetonated && Round.IsStarted)
                SpawnGOCCaptureTeam();
        }

        private IEnumerator<float> SerpentsHandSpawnCoroutine()
        {
            yield return Timing.WaitForSeconds(720f);

            if (!PluginConfig.EnableSerpentsHand) yield break;
            if (Warhead.IsDetonated || Round.IsEnded || !Round.IsStarted || !PluginConfig.EnableSpecialTeamRespawn)
                yield break;

            while (Player.List.Count(p => p.IsDead) < 4 && !Round.IsEnded && !Warhead.IsDetonated && Round.IsStarted)
            {
                yield return Timing.WaitForSeconds(10f);
            }

            if (!Round.IsEnded && !Warhead.IsDetonated && Round.IsStarted)
                SpawnSerpentsHand();
        }

        private IEnumerator<float> DeltaLegionSpawnCoroutine()
        {
            yield return Timing.WaitForSeconds(900f);

            if (!PluginConfig.EnableDeltaLegion) yield break;
            if (Warhead.IsDetonated || Round.IsEnded || !Round.IsStarted || !PluginConfig.EnableSpecialTeamRespawn)
                yield break;

            while (Player.List.Count(p => p.IsDead) < 5 && !Round.IsEnded && !Warhead.IsDetonated && Round.IsStarted)
            {
                yield return Timing.WaitForSeconds(10f);
            }

            if (!Round.IsEnded && !Warhead.IsDetonated && Round.IsStarted)
                SpawnDeltaLegion();
        }

        private IEnumerator<float> AlphaNineSpawnCoroutine()
        {
            while (!Warhead.IsDetonated && !Round.IsEnded && Round.IsStarted)
            {
                yield return Timing.WaitForSeconds(1f);
            }

            if (Warhead.IsDetonated)
            {
                yield return Timing.WaitForSeconds(120f);

                if (Round.IsEnded || !Round.IsStarted)
                    yield break;

                if (PluginConfig.EnableAlphaNine)
                    SpawnAlphaNine();
            }
            else
            {
                yield return Timing.WaitForSeconds(1080f);

                if (Warhead.IsDetonated || Round.IsEnded || !Round.IsStarted || !PluginConfig.EnableSpecialTeamRespawn)
                    yield break;

                while (Player.List.Count(p => p.IsDead) < 5 && !Round.IsEnded && Round.IsStarted)
                {
                    yield return Timing.WaitForSeconds(10f);
                }

                if (!Round.IsEnded && Round.IsStarted && PluginConfig.EnableAlphaNine)
                    SpawnAlphaNine();
            }
        }
        #endregion

        #region 事件处理
        public void OnHurting(HurtingEventArgs ev)
        {
            if (ev.Attacker == null || ev.Player == null || ev.DamageHandler == null)
                return;

            if (PluginConfig.EnableScp049DamageOverride &&
                ev.DamageHandler.Type == DamageType.Scp049 &&
                ev.Attacker != null &&
                ev.Attacker.Role.Type == RoleTypeId.Scp049)
            {
                ev.Amount = 100f;
            }

            if (PluginConfig.EnableScp106DamageOverride &&
                ev.DamageHandler.Type == DamageType.Scp106 &&
                ev.Attacker != null &&
                ev.Attacker.Role.Type == RoleTypeId.Scp106)
            {
                ev.Amount = 70f;
            }

            if (PluginConfig.EnableScp106Teleport &&
                ev.DamageHandler.Type == DamageType.Scp106 &&
                ev.Attacker != null &&
                ev.Attacker.Role.Type == RoleTypeId.Scp106)
            {
                ev.Player.EnableEffect<Effects.Corroding>(300f);
                Vector3 fixedPocketPosition = new Vector3(-0.341f, -298.294f, -0.214f);
                ev.Player.Teleport(fixedPocketPosition);
            }

            if (PluginConfig.EnableCustomFactionSystem)
            {
                CustomFaction attackerFaction = GetPlayerFaction(ev.Attacker);
                CustomFaction targetFaction = GetPlayerFaction(ev.Player);

                string attackerCustomInfo = ev.Attacker?.CustomInfo ?? "";
                string targetCustomInfo = ev.Player?.CustomInfo ?? "";

                bool allowDamage = true;

                if (attackerFaction == targetFaction && attackerFaction != CustomFaction.None)
                {
                    allowDamage = false;
                    if (PluginConfig.Debug) Log.Debug($"阻止同阵营伤害: {ev.Attacker.Nickname}({attackerFaction}) -> {ev.Player.Nickname}({targetFaction})");
                }

                if (attackerCustomInfo == "GOC收容部队")
                {
                    if (targetFaction != CustomFaction.SCP && targetCustomInfo != "蛇之手")
                    {
                        allowDamage = false;
                        if (PluginConfig.Debug) Log.Debug($"阻止GOC攻击非SCP/蛇之手目标: {ev.Attacker.Nickname} -> {ev.Player.Nickname}({targetFaction})");
                    }
                }
                else if (targetCustomInfo == "GOC收容部队")
                {
                    if (attackerFaction != CustomFaction.SCP && attackerCustomInfo != "蛇之手")
                    {
                        allowDamage = false;
                        if (PluginConfig.Debug) Log.Debug($"阻止非SCP/蛇之手攻击GOC: {ev.Attacker.Nickname}({attackerFaction}) -> {ev.Player.Nickname}");
                    }
                }

                if (attackerCustomInfo == "蛇之手")
                {
                    if (targetFaction == CustomFaction.SCP)
                    {
                        allowDamage = false;
                        if (PluginConfig.Debug) Log.Debug($"阻止蛇之手攻击SCP阵营: {ev.Attacker.Nickname} -> {ev.Player.Nickname}({targetFaction})");
                    }
                }
                else if (targetCustomInfo == "蛇之手")
                {
                    if (attackerFaction == CustomFaction.SCP)
                    {
                        allowDamage = false;
                        if (PluginConfig.Debug) Log.Debug($"阻止SCP阵营攻击蛇之手: {ev.Attacker.Nickname}({attackerFaction}) -> {ev.Player.Nickname}");
                    }
                }

                ev.IsAllowed = allowDamage;

                if (PluginConfig.Debug && allowDamage)
                {
                    Log.Debug($"允许伤害: {ev.Attacker.Nickname}({attackerFaction}/{attackerCustomInfo}) -> {ev.Player.Nickname}({targetFaction}/{targetCustomInfo})");
                }
            }

            if (PluginConfig.EnableScp2818 &&
                ev.Attacker != null &&
                ev.Attacker.CurrentItem != null &&
                Scp2818Items.Contains(ev.Attacker.CurrentItem.Serial))
            {
                ev.Amount = 2500;
                Timing.CallDelayed(0.05f, () =>
                {
                    if (ev.Attacker != null && ev.Attacker.IsAlive)
                    {
                        ev.Attacker.Kill("\n你<color=#FF0000>一命</color>换<color=#FF0000>一命</color>");
                    }
                });
                return;
            }

            if (PluginConfig.EnableScp999 && ev.Attacker != null && Scp999Players.Contains(ev.Attacker))
            {
                if (ev.Player == null)
                {
                    if (PluginConfig.Debug) Log.Debug("[SCP999] 攻击非玩家目标 - 已阻止");
                    ev.IsAllowed = false;
                    return;
                }

                ev.Player.Heal(5f);
                ev.Attacker.ShowHitMarker();
                ev.IsAllowed = false;
                return;
            }

            if (PluginConfig.EnableScp181 && Scp181Players.Contains(ev.Player) &&
                Random.Range(1, 5) == 1)
            {
                ev.IsAllowed = false;
            }
        }

        public void OnDying(DyingEventArgs ev)
        {
            if (PluginConfig.EnableScp008 &&
                ev.Attacker != null &&
                Scp008Players.Contains(ev.Attacker) &&
                ev.Player != null)
            {
                ev.IsAllowed = false;

                var position = ev.Player.Position;

                Timing.CallDelayed(0.1f, () =>
                {
                    if (ev.Player != null && ev.Player.IsConnected)
                    {
                        SpawnScp008(ev.Player);
                        ev.Player.Teleport(position);
                        ev.Player.ShowHint(GetFormattedHint($"<color={_colorScp}>你已被SCP-008感染!</color>"), 5f);
                    }
                });
                return;
            }

            if (PluginConfig.EnableScp008 && Scp008Players.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                ev.Player.MaxHealth = 100;
                Scp008Players.Remove(ev.Player);

                while (ev.Player.IsEffectActive<Effects.Scp207>())
                {
                    ev.Player.DisableEffect<Effects.Scp207>();
                }

                Cassie.MessageTranslated("s c p 0 0 8 contained successfully", "SCP-008已成功收容");
            }

            if (PluginConfig.EnableScp181 && Scp181Players.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                Scp181Players.Remove(ev.Player);
                Cassie.MessageTranslated("s c p 1 8 1 contained successfully", "SCP-181已成功收容");
            }

            if (PluginConfig.EnableScp999 && Scp999Players.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                Scp999Players.Remove(ev.Player);
                Cassie.MessageTranslated("s c p 9 9 9 contained successfully", "SCP-999已成功收容");
            }

            if (PluginConfig.EnableScp682 && ev.Player != null)
            {
                if (Scp682Players.Contains(ev.Player) &&
                    ev.DamageHandler.Type != DamageType.Warhead &&
                    ev.DamageHandler.Type != DamageType.Crushed)
                {
                    Scp682Players.Remove(ev.Player);
                    ev.IsAllowed = false;
                    ev.Player.Health = 1;

                    var position = ev.Player.Position;

                    ev.Player.ShowHint(GetFormattedHint($"你受到<color=#FF0000>致命伤</color>，开始蜕变第二形态，<color=#00FF00>期间无敌</color>"), 5f);

                    Timing.CallDelayed(3f, () =>
                    {
                        if (ev.Player != null && ev.Player.IsConnected)
                        {
                            SpawnScp6821(ev.Player);
                            ev.Player.Teleport(position);
                        }
                    });
                    return;
                }

                if (Scp6821Players.Contains(ev.Player) &&
                    ev.DamageHandler.Type != DamageType.Warhead &&
                    ev.DamageHandler.Type != DamageType.Crushed)
                {
                    Scp6821Players.Remove(ev.Player);
                    ev.IsAllowed = false;
                    ev.Player.Health = 1;

                    var position = ev.Player.Position;

                    ev.Player.ShowHint(GetFormattedHint($"你受到<color=#FF0000>致命伤</color>，开始蜕变第三形态，<color=#00FF00>期间无敌</color>"), 5f);

                    Timing.CallDelayed(3f, () =>
                    {
                        if (ev.Player != null && ev.Player.IsConnected)
                        {
                            SpawnScp6822(ev.Player);
                            ev.Player.Teleport(position);
                        }
                    });
                    return;
                }

                if (Scp6822Players.Contains(ev.Player))
                {
                    ResetPlayerAttributes(ev.Player);
                    Scp6822Players.Remove(ev.Player);
                    Cassie.MessageTranslated("S C P 6 8 2 contained successful", "SCP-682已被收容");
                }
            }

            if (PluginConfig.EnableScp3114 && Scp3114Players.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                Scp3114Players.Remove(ev.Player);
                Cassie.MessageTranslated("s c p 3 1 1 4 contained successfully", "SCP-3114已成功收容");
            }

            if (PluginConfig.EnableScp035 && Scp035Players.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                Vector3 deathPosition = ev.Player.Position;

                Scp035Players.Remove(ev.Player);

                if (_scp035ReviveCount < 3)
                {
                    _scp035ReviveCount++;
                    Timing.CallDelayed(1f, () =>
                    {
                        if (deathPosition != Vector3.zero)
                        {
                            Pickup item = Pickup.CreateAndSpawn(ItemType.SCP268, deathPosition);
                            Scp035Items.Add(item.Serial);
                            Log.Debug(string.Format("SCP-035面具在死亡位置生成: {0}", deathPosition));
                        }
                    });
                }

                Cassie.MessageTranslated("S C P 0 3 5 contained successful", "SCP-035已被收容面具已掉落");
            }

            if (ChaosFastResponsePlayers.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                ChaosFastResponsePlayers.Remove(ev.Player);
            }

            if (FoundationFastResponsePlayers.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                FoundationFastResponsePlayers.Remove(ev.Player);
            }

            if (GOCCapturePlayers.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                GOCCapturePlayers.Remove(ev.Player);
            }

            if (SerpentsHandPlayers.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                SerpentsHandPlayers.Remove(ev.Player);
            }

            if (DeltaLegionPlayers.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                DeltaLegionPlayers.Remove(ev.Player);
            }

            if (AlphaNinePlayers.Contains(ev.Player))
            {
                ResetPlayerAttributes(ev.Player);
                AlphaNinePlayers.Remove(ev.Player);
            }

            Timing.CallDelayed(0.5f, () =>
            {
                if (ev.Player != null && ev.Player.IsConnected && !ev.Player.IsAlive)
                {
                    StopPersistentHint(ev.Player);
                    StartPersistentHint(ev.Player);
                }
            });
        }

        public void OnInteractingDoor(InteractingDoorEventArgs ev)
        {
            if (PluginConfig.EnableScp181 &&
                Scp181Players.Contains(ev.Player) &&
                ev.Door.Type != DoorType.Scp079First &&
                ev.Door.Type != DoorType.Scp079Second &&
                Random.Range(1, 5) == 1)
            {
                ev.IsAllowed = true;
                ev.Player.ShowHint(GetFormattedHint($"<color={_colorSpecial}>幸运地直接开启了门禁</color>"), 5f);
            }
        }

        public void OnChangedItem(ChangedItemEventArgs ev)
        {
            if (PluginConfig.EnableScp999 && Scp999Players.Contains(ev.Player))
            {
                _scp999FlashlightActive = (ev.Item != null && ev.Item.Type == ItemType.Flashlight);
            }
        }

        public void OnItemAdded(ItemAddedEventArgs ev)
        {
            if (PluginConfig.EnableScp2818 && ev.Item != null && Scp2818Items.Contains(ev.Item.Serial))
            {
                ev.Player.ShowHint(GetFormattedHint($"你捡起了<color=#FF0000>SCP-2818</color>，可以用<color=#FF0000>你的生命</color>换一次<color=#FF0000>毁灭伤害</color>"), 5f);
            }

            if (PluginConfig.EnableScp035 && ev.Item != null && Scp035Items.Contains(ev.Item.Serial))
            {
                Vector3 originalPosition = ev.Player.Position;
                List<ItemType> originalItems = ev.Player.Items.Select(i => i.Type).ToList();

                SpawnScp035(ev.Player);
                ev.Player.Position = originalPosition;

                foreach (var itemType in originalItems)
                {
                    if (itemType != ItemType.SCP268)
                    {
                        ev.Player.AddItem(itemType);
                    }
                }

                ev.Player.RemoveItem(ev.Item);
            }
        }

        public void OnPickingUpItem(PickingUpItemEventArgs ev)
        {
            if (PluginConfig.EnableScp035 && Scp035Items.Contains(ev.Pickup.Serial))
            {
                ev.IsAllowed = true;
            }

            if (PluginConfig.EnableScp999 &&
                Scp999Players.Contains(ev.Player) &&
                (Scp2818Items.Contains(ev.Pickup.Serial) || Scp035Items.Contains(ev.Pickup.Serial)))
            {
                ev.IsAllowed = false;
                ev.Player.ShowHint(GetFormattedHint($"<color=#FF0000>作为SCP-999，你无法拾取SCP-2818或SCP-035</color>"), 5f);

                if (PluginConfig.Debug)
                {
                    Log.Debug(string.Format("阻止SCP-999玩家 {0} 拾取特殊物品: {1}", ev.Player.Nickname, ev.Pickup.Type));
                }
            }

            if (PluginConfig.EnableScp035 && PluginConfig.EnableScp3114 && Scp035Items.Contains(ev.Pickup.Serial))
            {
                if (Scp3114Players.Contains(ev.Player))
                {
                    ev.IsAllowed = false;
                    ev.Player.ShowHint(GetFormattedHint($"<color=#FF0000>作为SCP-3114，你无法拾取SCP-035</color>"), 5f);

                    if (PluginConfig.Debug)
                    {
                        Log.Debug(string.Format("阻止SCP-3114玩家 {0} 拾取SCP-035", ev.Player.Nickname));
                    }
                }
            }
        }

        public void OnDroppingItem(DroppingItemEventArgs ev)
        {
            if (PluginConfig.EnableScp035 && ev.Item != null && Scp035Items.Contains(ev.Item.Serial))
            {
                ev.IsAllowed = false;
            }
        }

        public void OnVoiceChatting(VoiceChattingEventArgs ev)
        {
            if (PluginConfig.EnableScp035 && Scp035Players.Contains(ev.Player))
            {
                ev.Player.VoiceChannel = VoiceChatChannel.ScpChat;
            }
        }

        private void OnChangingRole(ChangingRoleEventArgs ev)
        {
            bool wasScp008 = Scp008Players.Contains(ev.Player);
            bool wasScp181 = Scp181Players.Contains(ev.Player);
            bool wasScp999 = Scp999Players.Contains(ev.Player);
            bool wasScp682 = Scp682Players.Contains(ev.Player);
            bool wasScp6821 = Scp6821Players.Contains(ev.Player);
            bool wasScp6822 = Scp6822Players.Contains(ev.Player);
            bool wasScp3114 = Scp3114Players.Contains(ev.Player);
            bool wasScp035 = Scp035Players.Contains(ev.Player);

            bool wasChaosFast = ChaosFastResponsePlayers.Contains(ev.Player);
            bool wasFoundationFast = FoundationFastResponsePlayers.Contains(ev.Player);
            bool wasGOC = GOCCapturePlayers.Contains(ev.Player);
            bool wasSerpents = SerpentsHandPlayers.Contains(ev.Player);
            bool wasDelta = DeltaLegionPlayers.Contains(ev.Player);
            bool wasAlpha = AlphaNinePlayers.Contains(ev.Player);

            if (PluginConfig.TerminateScp0492 && ev.NewRole == RoleTypeId.Scp0492)
            {
                Timing.CallDelayed(0.1f, () =>
                {
                    if (ev.Player != null && ev.Player.IsConnected)
                    {
                        ev.Player.Kill("SCP-049-2已被系统清除");
                        ev.Player.ShowHint(GetFormattedHint($"<color=red>SCP-049-2已被禁用!</color>"), 5f);
                        Cassie.MessageTranslated("warning scp 0 4 9 dash 2 has been terminated", "警告：SCP-049-2已被清除");
                    }
                });
                ev.IsAllowed = false;
                return;
            }

            ResetPlayerAttributes(ev.Player);

            Timing.CallDelayed(0.5f, () =>
            {
                if (ev.Player != null && ev.Player.IsConnected && ev.Player.IsAlive)
                {
                    if (wasScp008) SpawnScp008(ev.Player);
                    else if (wasScp181) SpawnScp181(ev.Player);
                    else if (wasScp999) SpawnScp999(ev.Player);
                    else if (wasScp682) SpawnScp682(ev.Player);
                    else if (wasScp6821) SpawnScp6821(ev.Player);
                    else if (wasScp6822) SpawnScp6822(ev.Player);
                    else if (wasScp3114) SpawnScp3114(ev.Player);
                    else if (wasScp035) SpawnScp035(ev.Player);
                    else if (wasChaosFast) ChaosFastResponsePlayers.Add(ev.Player);
                    else if (wasFoundationFast) FoundationFastResponsePlayers.Add(ev.Player);
                    else if (wasGOC) GOCCapturePlayers.Add(ev.Player);
                    else if (wasSerpents) SerpentsHandPlayers.Add(ev.Player);
                    else if (wasDelta) DeltaLegionPlayers.Add(ev.Player);
                    else if (wasAlpha) AlphaNinePlayers.Add(ev.Player);

                    StartPersistentHint(ev.Player);
                }
            });
        }

        private void OnFinishingRecall(FinishingRecallEventArgs ev)
        {
            if (PluginConfig.BlockScp049Resurrection)
            {
                ev.IsAllowed = false;
                ev.Player.ShowHint(GetFormattedHint($"复活功能已被禁用!"), 5f);
                Cassie.MessageTranslated("warning scp 0 4 9 resurrection attempt blocked", "警告：SCP-049复活尝试已被阻止");
            }
        }
        #endregion

        #region 插件生命周期
        public override void OnEnabled()
        {
            base.OnEnabled();

            Instance = this;
            Plugin = this;
            PluginConfig = Config;

            try
            {
                Server.FriendlyFire = PluginConfig.EnableFriendlyFire;
                _isFriendlyFireSet = true;
                Log.Info($"友军伤害已{(PluginConfig.EnableFriendlyFire ? "开启" : "关闭")}");
            }
            catch (Exception ex)
            {
                Log.Error($"首次设置友军伤害失败，启动重试机制: {ex}");
                StartFriendlyFireRetry();
            }

            SubscribeEvents();

            if (PluginConfig.EnableScp999)
            {
                Timing.RunCoroutine(Scp999HealCoroutine());
            }

            Log.Info($"阵营插件加载成功 v{Version}");
        }

        public override void OnDisabled()
        {
            UnsubscribeEvents();

            if (_friendlyFireRetryCoroutine.IsRunning)
                Timing.KillCoroutines(_friendlyFireRetryCoroutine);

            foreach (var coroutineHandle in _playerHintCoroutines.Values)
            {
                Timing.KillCoroutines(coroutineHandle);
            }
            _playerHintCoroutines.Clear();

            Log.Info("阵营插件已卸载");
        }

        private void SubscribeEvents()
        {
            Exiled.Events.Handlers.Player.Hurting += OnHurting;
            Exiled.Events.Handlers.Player.Dying += OnDying;
            Exiled.Events.Handlers.Player.InteractingDoor += OnInteractingDoor;
            Exiled.Events.Handlers.Player.ChangedItem += OnChangedItem;
            Exiled.Events.Handlers.Player.ItemAdded += OnItemAdded;
            Exiled.Events.Handlers.Player.PickingUpItem += OnPickingUpItem;
            Exiled.Events.Handlers.Player.DroppingItem += OnDroppingItem;
            Exiled.Events.Handlers.Player.VoiceChatting += OnVoiceChatting;
            Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
            Exiled.Events.Handlers.Scp049.FinishingRecall += OnFinishingRecall;

            Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
            Exiled.Events.Handlers.Server.RoundEnded += OnRoundEnded;
            Exiled.Events.Handlers.Player.Left += OnPlayerLeft;
        }

        private void UnsubscribeEvents()
        {
            Exiled.Events.Handlers.Player.Hurting -= OnHurting;
            Exiled.Events.Handlers.Player.Dying -= OnDying;
            Exiled.Events.Handlers.Player.InteractingDoor -= OnInteractingDoor;
            Exiled.Events.Handlers.Player.ChangedItem -= OnChangedItem;
            Exiled.Events.Handlers.Player.ItemAdded -= OnItemAdded;
            Exiled.Events.Handlers.Player.PickingUpItem -= OnPickingUpItem;
            Exiled.Events.Handlers.Player.DroppingItem -= OnDroppingItem;
            Exiled.Events.Handlers.Player.VoiceChatting -= OnVoiceChatting;
            Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
            Exiled.Events.Handlers.Scp049.FinishingRecall -= OnFinishingRecall;

            Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
            Exiled.Events.Handlers.Server.RoundEnded -= OnRoundEnded;
            Exiled.Events.Handlers.Player.Left -= OnPlayerLeft;
        }

        private void OnRoundStarted()
        {
            _scp035ReviveCount = 0;

            _chaosSpawned = false;
            _foundationSpawned = false;
            _gocSpawned = false;
            _serpentsHandSpawned = false;
            _deltaLegionSpawned = false;
            _alphaNineSpawned = false;

            if (!_isFriendlyFireSet)
            {
                StartFriendlyFireRetry();
            }

            _scheduledSpawnsCoroutine = Timing.RunCoroutine(ScheduledSpawnsCoroutine());
            Timing.RunCoroutine(GOCCaptureSpawnCoroutine());
            Timing.RunCoroutine(SerpentsHandSpawnCoroutine());
            Timing.RunCoroutine(DeltaLegionSpawnCoroutine());
            Timing.RunCoroutine(AlphaNineSpawnCoroutine());

            if (!PluginConfig.EnableAutoSpawn)
                return;

            if (PluginConfig.EnableScp682 && Player.List.Count() > 13)
            {
                var nonScpPlayers = Player.List.Where(p => !p.IsScp).ToList();
                if (nonScpPlayers.Count > 0)
                {
                    var randomPlayer = nonScpPlayers[Random.Range(0, nonScpPlayers.Count)];
                    SpawnScp682(randomPlayer);
                }
            }

            if (PluginConfig.EnableScp3114 && Player.List.Count() > 9)
            {
                var dClassPlayers = Player.List.Where(p => p.Role.Type == RoleTypeId.ClassD).ToList();
                if (dClassPlayers.Count > 0)
                {
                    var randomDClass = dClassPlayers[Random.Range(0, dClassPlayers.Count)];
                    SpawnScp3114(randomDClass);
                }
            }

            foreach (var player in Player.List)
            {
                if (Scp181Players.Count < 1 && player.Role.Type == RoleTypeId.ClassD)
                {
                    SpawnScp181(player);
                }

                if (Scp999Players.Count < 1 && Player.List.Count > 5 && player.Role.Type == RoleTypeId.FacilityGuard)
                {
                    SpawnScp999(player);
                }

                if (Scp008Players.Count < 1 && Player.List.Count > 12 && player.Role.Type == RoleTypeId.Scientist)
                {
                    SpawnScp008(player);
                }

                StartPersistentHint(player);
            }

            if (PluginConfig.Debug) Log.Debug("已初始化所有特殊角色和阵营");
        }
        #endregion

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            foreach (var coroutineHandle in _playerHintCoroutines.Values)
            {
                Timing.KillCoroutines(coroutineHandle);
            }
            _playerHintCoroutines.Clear();
        }

        private void OnPlayerLeft(LeftEventArgs ev)
        {
            if (ev.Player == null) return;
            ResetPlayerAttributes(ev.Player);
        }

        public IEnumerator<float> Scp999HealCoroutine()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(1f);
                if (!_scp999FlashlightActive || Scp999Players.Count == 0)
                    continue;

                Player scp999Player = Scp999Players[0];
                foreach (var player in Player.List)
                {
                    if (player == scp999Player)
                        continue;

                    if (Vector3.Distance(player.Position, scp999Player.Position) <= 10f)
                    {
                        player.Heal(5);
                        scp999Player.ShowHitMarker();
                    }
                }
            }
        }


        #region SCP命令处理器
        [CommandHandler(typeof(RemoteAdminCommandHandler))]
        public class SpawnScp008Command : ICommand
        {
            public string Command => "spawn008";
            public string[] Aliases => new string[0];
            public string Description => "生成SCP-008角色";

            public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
            {
                if (arguments.Count < 1)
                {
                    response = "缺少玩家ID或昵称";
                    return false;
                }

                Player player = Player.Get(arguments.Array[arguments.Offset + 0]);
                if (player != null)
                {
                    Plugin.SpawnScp008(player);
                    response = "成功生成SCP-008!";
                    return true;
                }

                response = "找不到指定玩家";
                return false;
            }
        }

        [CommandHandler(typeof(RemoteAdminCommandHandler))]
        public class SpawnScp181Command : ICommand
        {
            public string Command => "spawn181";
            public string[] Aliases => new string[0];
            public string Description => "生成SCP-181角色";

            public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
            {
                if (arguments.Count < 1)
                {
                    response = "缺少玩家ID或昵称";
                    return false;
                }

                Player player = Player.Get(arguments.Array[arguments.Offset + 0]);
                if (player != null)
                {
                    Plugin.SpawnScp181(player);
                    response = "成功生成SCP-181!";
                    return true;
                }

                response = "找不到指定玩家";
                return false;
            }
        }

        [CommandHandler(typeof(RemoteAdminCommandHandler))]
        public class SpawnScp999Command : ICommand
        {
            public string Command => "spawn999";
            public string[] Aliases => new string[0];
            public string Description => "生成SCP-999角色";

            public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
            {
                if (arguments.Count < 1)
                {
                    response = "缺少玩家ID或昵称";
                    return false;
                }

                Player player = Player.Get(arguments.Array[arguments.Offset + 0]);
                if (player != null)
                {
                    Plugin.SpawnScp999(player);
                    response = "成功生成SCP-999!";
                    return true;
                }

                response = "找不到指定玩家";
                return false;
            }
        }

        [CommandHandler(typeof(RemoteAdminCommandHandler))]
        public class SpawnScp3114Command : ICommand
        {
            public string Command => "spawn3114";
            public string[] Aliases => new string[0];
            public string Description => "生成SCP-3114角色";

            public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
            {
                if (arguments.Count < 1)
                {
                    response = "缺少玩家ID或昵称";
                    return false;
                }

                Player player = Player.Get(arguments.Array[arguments.Offset + 0]);
                if (player != null)
                {
                    Plugin.SpawnScp3114(player);
                    response = "成功生成SCP-3114!";
                    return true;
                }

                response = "找不到指定玩家";
                return false;
            }
        }

        [CommandHandler(typeof(RemoteAdminCommandHandler))]
        public class SpawnScp682Command : ICommand
        {
            public string Command => "spawn682";
            public string[] Aliases => new string[0];
            public string Description => "生成SCP-682角色";

            public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
            {
                if (arguments.Count < 1)
                {
                    response = "缺少玩家ID或昵称";
                    return false;
                }

                Player player = Player.Get(arguments.Array[arguments.Offset + 0]);
                if (player != null)
                {
                    Plugin.SpawnScp682(player);
                    response = "成功生成SCP-682!";
                    return true;
                }

                response = "找不到指定玩家";
                return false;
            }
        }

        [CommandHandler(typeof(RemoteAdminCommandHandler))]
        public class SpawnScp035Command : ICommand
        {
            public string Command => "spawn035";
            public string[] Aliases => new string[0];
            public string Description => "生成SCP-035角色";

            public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
            {
                if (arguments.Count < 1)
                {
                    response = "缺少玩家ID或昵称";
                    return false;
                }

                Player player = Player.Get(arguments.Array[arguments.Offset + 0]);
                if (player != null)
                {
                    Plugin.SpawnScp035(player);
                    response = "成功生成SCP-035!";
                    return true;
                }

                response = "找不到指定玩家";
                return false;
            }
        }
        #endregion
    }

    public class Config : IConfig
    {
        [Description("是否启用插件")]
        public bool IsEnabled { get; set; } = true;

        [Description("是否开启友军伤害")]
        public bool EnableFriendlyFire { get; set; } = true;

        [Description("调试模式")]
        public bool Debug { get; set; } = false;

        #region SCP功能配置
        [Description("是否覆盖SCP-049的伤害为100")]
        public bool EnableScp049DamageOverride { get; set; } = true;

        [Description("是否覆盖SCP-106的伤害为70")]
        public bool EnableScp106DamageOverride { get; set; } = true;

        [Description("是否启用SCP-106攻击时传送")]
        public bool EnableScp106Teleport { get; set; } = true;

        [Description("是否阻止SCP-049复活")]
        public bool BlockScp049Resurrection { get; set; } = true;

        [Description("是否在生成时处死SCP-049-2")]
        public bool TerminateScp0492 { get; set; } = true;
        #endregion

        #region 特殊角色开关
        [Description("是否启用SCP-008")]
        public bool EnableScp008 { get; set; } = true;

        [Description("是否启用SCP-181")]
        public bool EnableScp181 { get; set; } = true;

        [Description("是否启用SCP-999")]
        public bool EnableScp999 { get; set; } = true;

        [Description("SCP-999生命值")]
        public int Scp999Health { get; set; } = 2000;

        [Description("SCP-999缩放比例")]
        public float Scp999Scale { get; set; } = 0.4f;

        [Description("是否启用SCP-682")]
        public bool EnableScp682 { get; set; } = true;

        [Description("是否启用SCP-3114")]
        public bool EnableScp3114 { get; set; } = true;

        [Description("是否启用SCP-035")]
        public bool EnableScp035 { get; set; } = true;

        [Description("是否启用SCP-2818")]
        public bool EnableScp2818 { get; set; } = true;

        [Description("是否自动生成特殊角色")]
        public bool EnableAutoSpawn { get; set; } = true;
        #endregion

        #region 特殊阵营开关
        [Description("是否启用混沌快速支援部队")]
        public bool EnableChaosFastResponse { get; set; } = true;

        [Description("是否启用基金会快速支援部队")]
        public bool EnableFoundationFastResponse { get; set; } = true;

        [Description("是否启用GOC收容部队")]
        public bool EnableGOCCapture { get; set; } = true;

        [Description("是否启用蛇之手")]
        public bool EnableSerpentsHand { get; set; } = true;

        [Description("是否启用德尔塔军团")]
        public bool EnableDeltaLegion { get; set; } = true;

        [Description("是否启用Alpha-9")]
        public bool EnableAlphaNine { get; set; } = true;

        [Description("是否启用特殊阵营刷新")]
        public bool EnableSpecialTeamRespawn { get; set; } = true;
        #endregion

        [Description("是否启用自定义阵营系统")]
        public bool EnableCustomFactionSystem { get; set; } = true;

        #region 统一提示样式配置
        [Description("提示垂直偏移行数")]
        public int HintVerticalOffset { get; set; } = 15;

        [Description("提示水平偏移空格数")]
        public int HintHorizontalOffset { get; set; } = 0;

        [Description("提示字体大小百分比 (100为正常大小)")]
        public int HintFontSize { get; set; } = 50;

        [Description("提示显示持续时间 (秒)")]
        public float HintDuration { get; set; } = 3.5f;

        [Description("提示刷新间隔 (秒)")]
        public float HintRefreshInterval { get; set; } = 3f;

        [Description("是否启用角色提示系统")]
        public bool EnableRoleHints { get; set; } = true;
        #endregion
    }
}