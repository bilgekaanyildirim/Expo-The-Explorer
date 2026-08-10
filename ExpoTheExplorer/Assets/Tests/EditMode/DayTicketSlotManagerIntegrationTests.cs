using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.TicketSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Regression coverage for a bug that PR-6.5/PR-6.6's tests never caught: no test wired a
    // REAL TicketSlotManager to a REAL DayTicketSequenceProvider end-to-end. TicketSlotManager
    // used to pre-buffer a lookahead queue (a leftover from the pre-Day-system BoardDistributor
    // noise-leak feature), which made it pull far more tickets from the provider than
    // ticketsRequiredForDay over the course of a day -- a fixed-length authored ticketSequence
    // could never satisfy that, and the very first FillEmptySlots() call threw. The buffer was
    // removed (TicketSlotManager.cs); this test proves the fix by running an actual day
    // end-to-end and checking the provider is asked for exactly N tickets, never more.
    public class DayTicketSlotManagerIntegrationTests
    {
        private readonly List<UnityEngine.Object> spawned = new();

        private FoodCatalog catalog;
        private GameConfig gameConfig;
        private TicketGenerationConfig ticketConfig;
        private BoardDistributionConfig boardConfig;

        [SetUp]
        public void SetUp()
        {
            var main = CreateFoodItem("main");

            catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(catalog);
            SetItemsList(catalog, main);

            gameConfig = ScriptableObject.CreateInstance<GameConfig>();
            spawned.Add(gameConfig);

            ticketConfig = CreateTicketGenerationConfig();
            boardConfig = ScriptableObject.CreateInstance<BoardDistributionConfig>();
            spawned.Add(boardConfig);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) UnityEngine.Object.DestroyImmediate(o);
            spawned.Clear();
        }

        [Test]
        public void FullDayLifecycle_RealTicketSlotManagerAndProvider_RequestsExactlyNTickets_NeverThrows()
        {
            const int ticketsRequiredForDay = 10;

            var result = DayContentGenerator.Generate(catalog, gameConfig, ticketConfig, boardConfig, null, ticketsRequiredForDay, seed: 1);
            Assert.AreEqual(ticketsRequiredForDay, result.TicketSequence.Length);

            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = ticketsRequiredForDay,
                    ticketSequence = result.TicketSequence,
                    boardTimeline = result.BoardTimeline,
                    hasRetryVariant = false,
                    retryVariant = null,
                },
            };
            var json = JsonUtility.ToJson(dayJson);
            var parsed = DayCatalogParser.ParseAll(new[] { new DayJsonFile("test", json) }, catalog);
            var day = parsed[0];

            var callCount = 0;
            var sequenceProvider = new DayTicketSequenceProvider(day.TicketSequence, ticketConfig, new TicketFactory(ticketConfig, new System.Random(1)));

            Ticket CountingProvider()
            {
                callCount++;
                return sequenceProvider.NextTicket();
            }

            var state = new GameState(gameConfig);
            var slotManager = new TicketSlotManager(state, CountingProvider, () => { });

            Assert.DoesNotThrow(() =>
            {
                slotManager.FillEmptySlots(); // consumes 3 of the N tickets

                for (var i = 0; i < ticketsRequiredForDay - GameState.TicketSlotCount; i++)
                {
                    slotManager.DeliverTicket(i % GameState.TicketSlotCount); // consumes the remaining N-3
                }
            });

            // Old bug: this would be ticketsRequiredForDay + lookaheadCount (e.g. 20 for N=10,
            // default UpcomingQueueSize=10), and the day would never even finish FillEmptySlots().
            Assert.AreEqual(ticketsRequiredForDay, callCount);

            // The authored sequence really does have exactly N entries -- nothing left over,
            // nothing missing.
            Assert.Throws<InvalidOperationException>(() => sequenceProvider.NextTicket());
        }

        private FoodItemConfig CreateFoodItem(string id)
        {
            var item = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return item;
        }

        private static void SetItemsList(FoodCatalog target, params FoodItemConfig[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty("items");
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private TicketGenerationConfig CreateTicketGenerationConfig()
        {
            var config = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawned.Add(config);

            var namesDatabase = new TextAsset(
                "[{\"id\":1,\"name\":\"Alice\",\"gender\":\"f\"},{\"id\":2,\"name\":\"Bob\",\"gender\":\"m\"}]");
            spawned.Add(namesDatabase);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("namesDatabase").objectReferenceValue = namesDatabase;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }
    }
}
