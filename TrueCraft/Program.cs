﻿using System.Net;
using System.Threading;
using TrueCraft.Core.World;
using TrueCraft.Core.TerrainGen;
using TrueCraft.Core.Logging;
using TrueCraft.API.Logging;
using TrueCraft.API.Server;
using System.IO;
using TrueCraft.Commands;
using TrueCraft.API.World;
using System;
using TrueCraft.Core;
using TrueCraft.API;
using YamlDotNet.Serialization;

namespace TrueCraft
{
    public class Program
    {
        public static Configuration Configuration;

        public static CommandManager CommandManager;

        public static MultiplayerServer Server;

        public static void Main(string[] args)
        {
            if (File.Exists("config.yaml"))
            {
                var deserializer = new Deserializer(ignoreUnmatched: true);
                using (var file = File.OpenText("config.yaml"))
                    Configuration = deserializer.Deserialize<Configuration>(file);
            }
            else
                Configuration = new Configuration();
            var serializer = new Serializer();
            using (var writer = new StreamWriter("config.yaml"))
                serializer.Serialize(writer, Configuration);

            Server = new MultiplayerServer();
            Server.AddLogProvider(new ConsoleLogProvider(LogCategory.Notice | LogCategory.Warning | LogCategory.Error | LogCategory.Debug));
            #if DEBUG
            Server.AddLogProvider(new FileLogProvider(new StreamWriter("packets.log", false), LogCategory.Packets));
            #endif
            if (Configuration.Debug.DeleteWorldOnStartup)
            {
                if (Directory.Exists("world"))
                    Directory.Delete("world", true);
            }
            if (Configuration.Debug.DeletePlayersOnStartup)
            {
                if (Directory.Exists("players"))
                    Directory.Delete("players", true);
            }
            IWorld world;
            try
            {
                world = World.LoadWorld("world");
                Server.AddWorld(world);
            }
            catch
            {
                world = new World("default", 1922464833, new StandardGenerator());
                world.BlockRepository = Server.BlockRepository;
                world.Save("world");
                Server.AddWorld(world);
                Server.Log(LogCategory.Notice, "Generating world around spawn point...");
                for (int x = -5; x < 5; x++)
                {
                    for (int z = -5; z < 5; z++)
                        world.GetChunk(new Coordinates2D(x, z));
                    int progress = (int)(((x + 5) / 10.0) * 100);
                    if (progress % 10 == 0)
                        Server.Log(LogCategory.Notice, "{0}% complete", progress + 10);
                }
                Server.Log(LogCategory.Notice, "Simulating the world for a moment...");
                for (int x = -5; x < 5; x++)
                {
                    for (int z = -5; z < 5; z++)
                    {
                        var chunk = world.GetChunk(new Coordinates2D(x, z));
                        for (byte _x = 0; _x < Chunk.Width; _x++)
                        {
                            for (byte _z = 0; _z < Chunk.Depth; _z++)
                            {
                                for (int _y = 0; _y < chunk.GetHeight(_x, _z); _y++)
                                {
                                    var coords = new Coordinates3D(x + _x, _y, z + _z);
                                    var data = world.GetBlockData(coords);
                                    var provider = world.BlockRepository.GetBlockProvider(data.ID);
                                    provider.BlockUpdate(data, data, Server, world);
                                }
                            }
                        }
                    }
                    int progress = (int)(((x + 5) / 10.0) * 100);
                    if (progress % 10 == 0)
                        Server.Log(LogCategory.Notice, "{0}% complete", progress + 10);
                }
            }
            CommandManager = new CommandManager();
            Server.ChatMessageReceived += HandleChatMessageReceived;
            Server.Start(new IPEndPoint(IPAddress.Parse(Configuration.ServerAddress), Configuration.ServerPort));
            Console.CancelKeyPress += HandleCancelKeyPress;
            while (true)
            {
                Thread.Sleep(1000 * 30); // TODO: Allow users to customize world save interval
                foreach (var w in Server.Worlds)
                {
                    w.Save();
                }
            }
        }

        static void HandleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Server.Stop();
        }

        static void HandleChatMessageReceived(object sender, ChatMessageEventArgs e)
        {
            if (e.Message.StartsWith("/"))
            {
                e.PreventDefault = true;
                var messageArray = e.Message.TrimStart('/')
                    .Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                CommandManager.HandleCommand(e.Client, messageArray[0], messageArray);
                return;
            }
        }
    }
}