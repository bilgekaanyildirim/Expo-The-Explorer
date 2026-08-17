using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.TicketSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class TicketEntryFactoryTests
    {
        private readonly List<Object> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        private FoodItemConfig CreateFood()
        {
            var food = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(food);
            return food;
        }

        private TicketGenerationConfig CreateConfig(bool withNamesDatabase = false)
        {
            var config = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("impatientTimeLimitSeconds").floatValue = 10f;
            serialized.FindProperty("normalTimeLimitSeconds").floatValue = 20f;
            serialized.FindProperty("patientTimeLimitSeconds").floatValue = 30f;

            if (withNamesDatabase)
            {
                var namesDatabase = new TextAsset(
                    "[{\"id\":1,\"name\":\"Alice\",\"gender\":\"f\"},{\"id\":2,\"name\":\"Bob\",\"gender\":\"m\"}]");
                spawned.Add(namesDatabase);
                serialized.FindProperty("namesDatabase").objectReferenceValue = namesDatabase;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private ResolvedTicketEntry CreateEntry(
            FoodItemConfig mainItem,
            FoodItemConfig sideItem = null,
            FoodItemConfig drinkItem = null,
            PatienceType patienceType = PatienceType.Normal,
            string customerNameOverride = null,
            float timeLimitSecondsOverride = 0f)
        {
            return new ResolvedTicketEntry(
                mainItem, sideItem, drinkItem, new List<Modification>(),
                patienceType, customerNameOverride, timeLimitSecondsOverride);
        }

        [Test]
        public void Create_WithCustomerNameOverride_UsesOverride()
        {
            var config = CreateConfig();
            var factory = new TicketFactory(config, new System.Random(1));
            var entry = CreateEntry(CreateFood(), customerNameOverride: "Overridden Name");

            var ticket = TicketEntryFactory.Create(entry, config.ToRuntimeSettings(), factory, 0);

            Assert.AreEqual("Overridden Name", ticket.CustomerName);
        }

        [Test]
        public void Create_WithoutOverride_UsesRandomNameFromConfig()
        {
            var config = CreateConfig(withNamesDatabase: true);
            var factory = new TicketFactory(config, new System.Random(1));
            var entry = CreateEntry(CreateFood(), customerNameOverride: null);

            var ticket = TicketEntryFactory.Create(entry, config.ToRuntimeSettings(), factory, 0);

            Assert.IsTrue(config.CustomerNames.Contains(ticket.CustomerName));
        }

        [Test]
        public void Create_WithTimeLimitOverride_UsesOverride()
        {
            var config = CreateConfig();
            var factory = new TicketFactory(config, new System.Random(1));
            var entry = CreateEntry(CreateFood(), patienceType: PatienceType.Impatient, customerNameOverride: "Whoever", timeLimitSecondsOverride: 99f);

            var ticket = TicketEntryFactory.Create(entry, config.ToRuntimeSettings(), factory, 0);

            Assert.AreEqual(99f, ticket.TimeLimitSeconds);
        }

        [Test]
        public void Create_WithoutTimeLimitOverride_UsesConfigDefaultForPatienceType()
        {
            var config = CreateConfig();
            var factory = new TicketFactory(config, new System.Random(1));
            var entry = CreateEntry(CreateFood(), patienceType: PatienceType.Patient, customerNameOverride: "Whoever", timeLimitSecondsOverride: 0f);

            var ticket = TicketEntryFactory.Create(entry, config.ToRuntimeSettings(), factory, 0);

            Assert.AreEqual(config.ToRuntimeSettings().PatientTimeLimitSeconds, ticket.TimeLimitSeconds);
        }

        [Test]
        public void Create_BuildsRequiredItemsInMainSideDrinkOrder_SkippingNullSideOrDrink()
        {
            var config = CreateConfig();
            var factory = new TicketFactory(config, new System.Random(1));
            var main = CreateFood();
            var drink = CreateFood();
            var entry = CreateEntry(main, sideItem: null, drinkItem: drink, customerNameOverride: "Whoever");

            var ticket = TicketEntryFactory.Create(entry, config.ToRuntimeSettings(), factory, 0);

            Assert.AreEqual(2, ticket.RequiredItems.Count);
            Assert.AreSame(main, ticket.RequiredItems[0]);
            Assert.AreSame(drink, ticket.RequiredItems[1]);
        }

        [Test]
        public void Create_PassesThroughArrivalSequence()
        {
            var config = CreateConfig();
            var factory = new TicketFactory(config, new System.Random(1));
            var entry = CreateEntry(CreateFood(), customerNameOverride: "Whoever");

            var ticket = TicketEntryFactory.Create(entry, config.ToRuntimeSettings(), factory, 42);

            Assert.AreEqual(42, ticket.ArrivalSequence);
        }
    }
}
