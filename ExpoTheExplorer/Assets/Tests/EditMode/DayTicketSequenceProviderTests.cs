using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.TicketSystem;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayTicketSequenceProviderTests
    {
        private readonly List<UnityEngine.Object> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) UnityEngine.Object.DestroyImmediate(o);
            spawned.Clear();
        }

        private FoodItemConfig CreateFood()
        {
            var food = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(food);
            return food;
        }

        private TicketGenerationConfig CreateConfig()
        {
            var config = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawned.Add(config);
            return config;
        }

        private ResolvedTicketEntry CreateEntry(FoodItemConfig mainItem, string customerNameOverride)
        {
            return new ResolvedTicketEntry(
                mainItem, null, null, new List<Modification>(),
                PatienceType.Normal, customerNameOverride, timeLimitSecondsOverride: 30f);
        }

        [Test]
        public void NextTicket_ReturnsEntriesInOrder_WithSequentialArrivalSequence()
        {
            var config = CreateConfig();
            var factory = new TicketFactory(config, new System.Random(1));
            var entries = new List<ResolvedTicketEntry>
            {
                CreateEntry(CreateFood(), "First"),
                CreateEntry(CreateFood(), "Second"),
                CreateEntry(CreateFood(), "Third"),
            };
            var provider = new DayTicketSequenceProvider(entries, config, factory);

            var first = provider.NextTicket();
            var second = provider.NextTicket();
            var third = provider.NextTicket();

            Assert.AreEqual("First", first.CustomerName);
            Assert.AreEqual(0, first.ArrivalSequence);
            Assert.AreEqual("Second", second.CustomerName);
            Assert.AreEqual(1, second.ArrivalSequence);
            Assert.AreEqual("Third", third.CustomerName);
            Assert.AreEqual(2, third.ArrivalSequence);
        }

        [Test]
        public void NextTicket_PastEndOfSequence_Throws()
        {
            var config = CreateConfig();
            var factory = new TicketFactory(config, new System.Random(1));
            var entries = new List<ResolvedTicketEntry> { CreateEntry(CreateFood(), "Only") };
            var provider = new DayTicketSequenceProvider(entries, config, factory);

            provider.NextTicket();

            Assert.Throws<InvalidOperationException>(() => provider.NextTicket());
        }
    }
}
