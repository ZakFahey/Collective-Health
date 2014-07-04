using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using System.Timers;
using System.IO;

namespace ColHealth
{
    public class Person {
        public string name { get; set; }
        public int damage { get; set; }
    }

    [ApiVersion(1, 16)]
    public class ColHealth : TerrariaPlugin {
        int[] totalHealth { get; set; }
        int killedTeam { get; set; }
        int[] lastHealth { get; set; }
        int endMsg = 0;
        int[] lastDmg { get; set; }
        bool colStarted { get; set; }
        byte[] notifyCooldown { get; set; }
        Timer notify = new Timer();
        Timer end = new Timer();
        List<string>[] playersAtFault = new List<string>[4];
        List<Person> highScore = new List<Person>();
        List<string> okTeams = new List<string>();
        public ColHealth(Main game) : base(game) {
        }

        public override void Initialize() {
            TShockAPI.GetDataHandlers.PlayerDamage += PlayerDamage;
            TShockAPI.GetDataHandlers.PlayerTeam += PlayerTeam;
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
            ServerApi.Hooks.GameUpdate.Register(this, OnUpdateEvent);
            TShockAPI.GetDataHandlers.PlayerUpdate += PlayerUpdate;
            colStarted = false;
            Commands.ChatCommands.Add(new Command("tshock.colhealth", colStart, "colstart") {
                HelpText = "Start a collective health hardcore survival challenge. Format: /colstart <Team1, Team2...>"
            });
            Commands.ChatCommands.Add(new Command("tshock.colhealth", giveHearts, "ghearts", "gheart", "giveheart", "givehearts")
            {
                HelpText = "Gives every player 15 Life Crystals."
            });
            /*Commands.ChatCommands.Add(new Command("tshock.colhealth", HPadd, "hpadd")
            {
                HelpText = "Increases the HP."
            });*/
            Commands.ChatCommands.Add(new Command("tshock.cancheckhp", checkHP, "checkhp", "checkhealth")
            {
                HelpText = "Check the health. Format: /checkhp <Team1, Team2...> or /checkhp all"
            });
            notify.Elapsed += new ElapsedEventHandler(notifyTimer);
            notify.Interval = 20;
            notify.AutoReset = true;
            end.Elapsed += new ElapsedEventHandler(endTimer);
            end.Interval = 1000;
            end.AutoReset = false;

            if (!Config.ReadConfig())
                Log.ConsoleError("Failed to read CreativeModeConfig.json. Consider generating a new config file.");
        }
        public override Version Version {
            get { return new Version("1.0"); }
        }
        public override string Name {
            get { return "Collective Health"; }
        }
        public override string Author {
            get { return "GameRoom"; }
        }
        public override string Description {
            get { return "Makes everyone have one large healthbar."; }
        }

        void PlayerTeam(object sender, TShockAPI.GetDataHandlers.PlayerTeamEventArgs e) {
            bool attempt = false;
            if (colStarted) {
                if (e.Team == 0) {
                    TShock.Players[e.PlayerId].DamagePlayer(999999);
                    attempt = true;
                }
                else if (totalHealth[e.Team] > 0)
                    TShock.Players[e.PlayerId].SendMessage(String.Format("Health: {0}", totalHealth[e.Team]), Main.teamColor[e.Team].R, Main.teamColor[e.Team].G, Main.teamColor[e.Team].B);
                else attempt = true;
            }
            if (attempt)
                foreach (TSPlayer player in TShock.Players)
                    if (player != null && player.Active && player.Group.ToString() == "superadmin")
                        player.SendInfoMessage(String.Format("{0} attempted to switch teams.", TShock.Players[e.PlayerId].Name));
        }

        void PlayerDamage(object sender, TShockAPI.GetDataHandlers.PlayerDamageEventArgs e) {
            sbyte plrTm = Convert.ToSByte(TShock.Players[e.ID].Team - 1);
            if (colStarted && plrTm != -1) {
                totalHealth[plrTm] -= e.Damage;
                if (endMsg == 0)
                    foreach (Person score in highScore)
                        if (score.name == TShock.Players[e.ID].Name) score.damage += e.Damage;
                if (totalHealth[plrTm] <= 0) {
                    totalHealth[plrTm] = 0;
                    if (endMsg == 0) {
                        killedTeam = plrTm;
                        lastDmg[plrTm] = e.ID;
                        end.Start();
                    }
                } else {
                    TShock.Players[e.ID].Heal();
                    if (!playersAtFault[plrTm].Contains(TShock.Players[e.ID].Name))
                        playersAtFault[plrTm].Add(TShock.Players[e.ID].Name);
                    byte totalPlayers = 1;
                    foreach (TSPlayer player in TShock.Players)
                        if (player != null && player.Active && !player.Dead && plrTm == player.Team - 1)
                            totalPlayers++;
                    if (notifyCooldown[plrTm] == 0 || totalHealth[plrTm] <= 40 * totalPlayers) {
                        notifyCooldown[plrTm] = 20;
                        StringBuilder sb = new StringBuilder();
                        foreach (string nm in playersAtFault[plrTm]) {
                            if (sb.ToString() != "") sb.Append(", ");
                            sb.Append(nm);
                        }
                        foreach (TSPlayer player in TShock.Players)
                            if (player != null && player.Active && plrTm == player.Team - 1)
                                player.SendMessage(String.Format("Health: {0}  ({1}, {2})", totalHealth[plrTm], sb.ToString(), totalHealth[plrTm] - lastHealth[plrTm]), Main.teamColor[plrTm + 1].R, Main.teamColor[plrTm + 1].G, Main.teamColor[plrTm + 1].B);
                        lastHealth[plrTm] = totalHealth[plrTm];
                        playersAtFault[plrTm].Clear();
                    }
                }
            }
        }

        void colStart(CommandArgs e) {
            if (colStarted) e.Player.SendErrorMessage("The game has already started.");
            else {
                StringBuilder badPlayers = new StringBuilder();
                List<int> bannedTeams = new List<int>();
                okTeams.Clear();
                bannedTeams.Add(0);
                if (e.Parameters.Contains("red")) okTeams.Add("red");
                else bannedTeams.Add(1);
                if (e.Parameters.Contains("green")) okTeams.Add("green");
                else bannedTeams.Add(2);
                if (e.Parameters.Contains("blue")) okTeams.Add("blue");
                else bannedTeams.Add(3);
                if (e.Parameters.Contains("yellow")) okTeams.Add("yellow");
                else bannedTeams.Add(4);
                if (bannedTeams.Count == 5)
                    bannedTeams.RemoveRange(1, 4);
                
                int[] playerCount = { 0, 0, 0, 0, 0 };
                foreach (TSPlayer player in TShock.Players)
                    if (player != null && player.Active && !player.Dead) {
                        playerCount[player.Team]++;
                        bool can = false;
                        foreach (int tm in bannedTeams)
                            if (player.Team == tm) can = true;
                        if (can) {
                            if (badPlayers.ToString() != "") badPlayers.Append(", ");
                            badPlayers.Append(player.Name);
                        }
                    }

                bool canGo = true;
                foreach (int tm in bannedTeams)
                    if (playerCount[tm] > 0) canGo = false;
                if (canGo) {
                    totalHealth = new int[4];
                    lastHealth = new int[4];
                    lastDmg = new int[4];
                    notifyCooldown = new byte[4];
                    notify.Start();
                    colStarted = true;
                    for (int i = 0; i < 4; i++) {
                        byte totalPlayers = 0;
                        foreach (TSPlayer player in TShock.Players)
                            if (player != null && player.Active && !player.Dead && i == player.Team - 1)
                                totalPlayers++;
                        totalHealth[i] = totalPlayers * Config.contents.HealthPerPerson;
                        lastHealth[i] = totalHealth[i];
                        notifyCooldown[i] = 0;
                        playersAtFault[i] = new List<string>();
                        foreach (TSPlayer player in TShock.Players)
                            if (player != null && player.Active && !player.Dead && i == player.Team - 1)
                                player.SendMessage(String.Format("The game is starting. You all have {0} health. If you change to an invalid team, you will immediately be killed.", totalHealth[i]), Color.Yellow);
                    }
                    TShock.Config.DisableBuild = false;
                    TSPlayer.Server.SetTime(true, 20000.0);
                } else TSPlayer.All.SendErrorMessage(String.Format("Game cannot start until everyone is on {0} team. ({1})", acceptedTeams(false), badPlayers.ToString()));
            }
        }

        void checkHP(CommandArgs e) {
            List<byte> listedTeams = new List<byte>();
            if (e.Parameters.Count != 0) {
                if (e.Parameters[0] != "all") {
                    if (e.Parameters.Contains("red")) listedTeams.Add(1);
                    if (e.Parameters.Contains("green")) listedTeams.Add(2);
                    if (e.Parameters.Contains("blue")) listedTeams.Add(3);
                    if (e.Parameters.Contains("yellow")) listedTeams.Add(4);
                } else {
                    foreach (TSPlayer player in TShock.Players)
                        if (player != null && player.Active && !player.Dead && !listedTeams.Contains(Convert.ToByte(player.Team)))
                            listedTeams.Add(Convert.ToByte(player.Team));
                }
            } else listedTeams.Add(Convert.ToByte(e.Player.Team));
            if (colStarted) {
                foreach(int whatTeam in listedTeams)
                    e.Player.SendMessage(String.Format("Health: {0}", totalHealth[whatTeam - 1]), Main.teamColor[whatTeam].R, Main.teamColor[whatTeam].G, Main.teamColor[whatTeam].B);
            }
            else if (e.Player.Team > 0) e.Player.SendErrorMessage("No game of collective survival has started.");
            else e.Player.SendErrorMessage("Get on a team!");
        }

        private void bc(string text, Color color) {
            TSPlayer.All.SendMessage(text, color);
            Console.WriteLine(text);
        }

        private void notifyTimer(object source, ElapsedEventArgs e) {
            for (int i = 0; i < 4; i++)
                if (notifyCooldown[i] > 0) notifyCooldown[i]--;
        }

        /*void HPadd(CommandArgs e) {
            int amt;
            if (e.Parameters.Count>0) amt = Convert.ToInt32(e.Parameters[0]);
            else amt = 50;
            if (colStarted) {
                totalHealth += amt;
                lastHealth = totalHealth;
                var text = String.Format("Health: {0}  (+{1})", totalHealth, amt);
            }
            else e.Player.SendErrorMessage("No game of collective survival has started.");
        }*/

        void OnUpdateEvent(EventArgs e) {
            if (colStarted) {
                for (int i = 0; i < Main.maxItems; i++ )
                    if (Main.item[i].active && (Main.item[i].name == "Life Crystal" || Main.item[i].name == "Life Fruit") && endMsg == 0) {
                        int increase = Config.contents.HealthPerLifeCrystal * Main.item[i].stack;
                        dynamic attribution = null;
                        foreach (TSPlayer player in TShock.Players)
                            if (player != null && player.Active && !player.Dead)
                                if (attribution == null || distance(Main.item[i].position.X, Main.item[i].position.Y, player.X, player.Y) < distance(Main.item[i].position.X, Main.item[i].position.Y, attribution.X, attribution.Y))
                                    attribution = player;
                        sbyte plrTm = Convert.ToSByte(attribution.Team - 1);
                        foreach (Person score in highScore)
                            if (score.name == attribution.Name) score.damage -= increase;
                        totalHealth[plrTm] += increase;
                        lastHealth[plrTm] = totalHealth[plrTm];
                        Main.item[i].active = false;
                        TSPlayer.All.SendData(PacketTypes.ItemDrop, "", i);
                        var text = String.Format("Health: {0}  ({1}, +{2})", totalHealth[plrTm], attribution.Name, increase);
                        foreach (TSPlayer player in TShock.Players)
                            if (player != null && player.Active && plrTm == player.Team - 1)
                                player.SendSuccessMessage(text);
                        Console.WriteLine(text);
                        notifyCooldown[plrTm] = 20;
                        break;
                    }
                for (int i = 0; i < 4; i++)
                    if (totalHealth[i] <= 0)
                        foreach (TSPlayer player in TShock.Players)
                            if (player != null && player.Active && !player.Dead && player.Team == i + 1)
                                player.DamagePlayer(999999);
            }
        }

        void giveHearts(CommandArgs e) {
            var ITM = TShock.Utils.GetItemByIdOrName("life crystal")[0];
            foreach (TSPlayer player in TShock.Players)
                if (player != null && player.Active && player.InventorySlotAvailable && !player.Dead)
                    player.GiveItem(ITM.type, ITM.name, ITM.width, ITM.height, 15);
        }

        private void endTimer(object source, ElapsedEventArgs e) {
            if (endMsg == 0) {
                bc(String.Format("The {0} team is dead. Everybody give a big thanks to {1} for taking the last hit and killing everyone.", TeamIDToColor(killedTeam), TShock.Players[lastDmg[killedTeam]].Name), Color.Yellow);
                int teamsAlive = 0;
                int teamsPlaying = 0;
                bool[] playerCount = { false, false, false, false };
                foreach (TSPlayer player in TShock.Players)
                    if (player != null && player.Active && player.Team > 0) {
                        playerCount[player.Team - 1] = true;
                        teamsPlaying++;
                        highScore.Sort((x, y) => x.damage.CompareTo(y.damage));
                    }
                for (var i = 0; i < 4; i++)
                    if (totalHealth[i] > 0 && playerCount[i]) teamsAlive++;
                if (teamsAlive == 0 || (teamsAlive <=1 && teamsPlaying > 1)) {
                    end.AutoReset = true;
                    end.Start();
                    endMsg++;
                }
            }
            else {
                if (endMsg == 1) {
                    bc("Leaderboards (by damage taken):", Color.Yellow);
                    end.Interval = 2000;
                }
                StringBuilder sb = new StringBuilder();
                for(int i = endMsg * 3 - 3; i < Math.Min(endMsg * 3, highScore.Count); i++) {
                    if (sb.ToString() != "") sb.Append(",  ");
                    sb.Append(String.Format("{0}.) {1} - {2}", i + 1, highScore[i].name, highScore[i].damage));
                }
                bc(sb.ToString(), Color.Yellow);
                if (endMsg * 3 >= highScore.Count) end.Stop();
                endMsg++;
            }
        }

        void OnGreetPlayer(GreetPlayerEventArgs e) {
            if (!highScore.Exists(x => x.name == TShock.Players[e.Who].Name))
                highScore.Add(new Person {name = TShock.Players[e.Who].Name, damage = 0} );
            if (colStarted) TShock.Players[e.Who].SendWarningMessage(String.Format("Get on {0} team!", acceptedTeams(true)));
        }

        double distance(float x1, float y1, float x2, float y2) {
            return Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2);
        }

        string TeamIDToColor(int ID) {
            switch (ID) {
                case 0: return "red";
                case 1: return "green";
                case 2: return "blue";
                case 3: return "yellow";
                default: return "";
            }
        }

        void PlayerUpdate(object sender, TShockAPI.GetDataHandlers.PlayerUpdateEventArgs e) {
            if (TShock.Players[e.PlayerId].Team == 0 && colStarted)
                TShock.Players[e.PlayerId].Teleport(Main.spawnTileX * 16, Main.spawnTileY * 16 - 48);
        }

        string acceptedTeams(bool deleteDeadTeams) {
            if (deleteDeadTeams)
                for (int i = 0; i < 4; i++)
                    if (totalHealth[i] == 0 && okTeams.Contains(TeamIDToColor(i))) okTeams.Remove(TeamIDToColor(i));

            StringBuilder allowedTeams = new StringBuilder();
            if (okTeams.Count == 0) allowedTeams.Append("a");
                else {
                    if (okTeams.Count == 2) allowedTeams.Append("either the ");
                    else allowedTeams.Append("the ");
                    for (int i = 0; i < okTeams.Count; i++) {
                        if (i > 0) {
                            if (okTeams.Count == 2) allowedTeams.Append(" ");
                            else allowedTeams.Append(", ");
                            if (i == okTeams.Count - 1) allowedTeams.Append("or ");
                        }
                        allowedTeams.Append(okTeams[i]);
                    }
                }
                return allowedTeams.ToString();
        }
    }
}