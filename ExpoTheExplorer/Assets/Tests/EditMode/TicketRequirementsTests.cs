using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // The one rule this project keeps having to spell out: what a ticket actually wants,
    // and the fact that modifications belong to the Main dish only.
    //
    // It got its own type in powerup-plan Adım 5 because it now has three consumers
    // (TraySlot.Matches, the Noise Clear powerup, Adım 6's auto-collect) -- and it gets its
    // own tests because of what it decides. A wrong answer here does not crash: it makes a
    // correct delivery cost a life, or lets a wrong one through. TraySystemTests covers
    // that outcome end-to-end; this file covers the rule itself, so a break points at the
    // rule instead of at the tray.
    public class TicketRequirementsTests
    {
        private readonly List<UnityEngine.Object> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in spawned) UnityEngine.Object.DestroyImmediate(asset);
            spawned.Clear();
        }

        // Same shape BoardDistributionTests uses: the category is a private [SerializeField],
        // so it is set through SerializedObject and the property-name string has to track
        // the field name.
        private FoodItemConfig Food(FoodCategory category, string id)
        {
            var config = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("category").enumValueIndex = (int)category;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private ModificationConfig Mod(string id)
        {
            var config = ScriptableObject.CreateInstance<ModificationConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            var idProperty = serialized.FindProperty("id");
            if (idProperty != null) idProperty.stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private static Ticket NewTicket(
            IReadOnlyList<FoodItemConfig> required,
            IReadOnlyList<Modification> modifications = null,
            TicketState ticketState = TicketState.Active)
        {
            return new Ticket(
                "Test",
                PatienceType.Normal,
                required,
                modifications ?? Array.Empty<Modification>(),
                timeLimitSeconds: 90f)
            {
                State = ticketState,
            };
        }

        // --- the rule itself -------------------------------------------------------------

        // THE case this type exists for. A ticket's modifications describe its main dish;
        // attaching them to the side would mean asking for fries with no pickles, which no
        // board item ever carries, so the order could never be fulfilled.
        [Test]
        public void KeyFor_AttachesTheTicketsModificationsToTheMainDishOnly()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var fries = Food(FoodCategory.Side, "fries");
            var noPickles = new Modification(Mod("pickles"), isAddition: false);
            var ticket = NewTicket(new[] { burger, fries }, new[] { noPickles });

            var mainKey = TicketRequirements.KeyFor(ticket, burger);
            var sideKey = TicketRequirements.KeyFor(ticket, fries);

            Assert.AreEqual(1, mainKey.Modifications.Count, "the main dish carries the ticket's modifications");
            Assert.AreEqual(0, sideKey.Modifications.Count, "a side never does");
        }

        // The key's own equality is order- and instance-independent (RequiredItemKey's job),
        // and this pins that the helper does not accidentally break it by handing over a
        // different list object each time.
        [Test]
        public void KeyFor_TheSameFoodOnTheSameTicket_ProducesEqualKeys()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var ticket = NewTicket(new[] { burger }, new[] { new Modification(Mod("cheese"), isAddition: true) });

            Assert.AreEqual(TicketRequirements.KeyFor(ticket, burger), TicketRequirements.KeyFor(ticket, burger));
        }

        // Two tickets asking for the same food with DIFFERENT modifications must not be
        // interchangeable -- this is what stops a plain burger satisfying a no-pickles one.
        [Test]
        public void KeyFor_TheSameFoodWithDifferentModifications_ProducesDifferentKeys()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var plain = NewTicket(new[] { burger });
            var modified = NewTicket(new[] { burger }, new[] { new Modification(Mod("pickles"), isAddition: false) });

            Assert.AreNotEqual(TicketRequirements.KeyFor(plain, burger), TicketRequirements.KeyFor(modified, burger));
        }

        // --- counts ----------------------------------------------------------------------

        // A dictionary rather than a set, because this consumer validates deliveries: one
        // cola does not satisfy a ticket asking for two.
        [Test]
        public void RequiredCounts_CountsDuplicatesRatherThanCollapsingThem()
        {
            var cola = Food(FoodCategory.Drink, "cola");
            var ticket = NewTicket(new[] { cola, cola });

            var counts = TicketRequirements.RequiredCounts(ticket);

            Assert.AreEqual(1, counts.Count, "one distinct key");
            Assert.AreEqual(2, counts[TicketRequirements.KeyFor(ticket, cola)]);
        }

        [Test]
        public void RequiredCounts_WithNoTicket_IsEmptyRatherThanNull()
        {
            Assert.IsNotNull(TicketRequirements.RequiredCounts(null));
            Assert.AreEqual(0, TicketRequirements.RequiredCounts(null).Count);
        }

        // --- the active-ticket set -------------------------------------------------------

        // A SET here, deliberately, and this test is the design of the Noise Clear powerup
        // written down: it asks "does any ticket in hand want one of these at all", never
        // "how many", which is what lets surplus copies of a wanted food survive a clear.
        [Test]
        public void ActiveTicketKeys_CollapsesDuplicatesAcrossSlots()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var cola = Food(FoodCategory.Drink, "cola");

            var state = NewState();
            state.TicketSlots[0] = NewTicket(new[] { burger, burger });
            state.TicketSlots[1] = NewTicket(new[] { burger, cola });

            var keys = TicketRequirements.ActiveTicketKeys(state);

            Assert.AreEqual(2, keys.Count, "burger and cola, however many times they were asked for");
        }

        [Test]
        public void ActiveTicketKeys_IgnoresDeliveredAndCancelledTickets()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var cola = Food(FoodCategory.Drink, "cola");

            var state = NewState();
            state.TicketSlots[0] = NewTicket(new[] { burger }, ticketState: TicketState.Delivered);
            state.TicketSlots[1] = NewTicket(new[] { cola }, ticketState: TicketState.Cancelled);

            Assert.AreEqual(0, TicketRequirements.ActiveTicketKeys(state).Count);
        }

        [Test]
        public void ActiveTicketKeys_WithNoState_IsEmptyRatherThanNull()
        {
            var keys = TicketRequirements.ActiveTicketKeys(null);

            Assert.IsNotNull(keys);
            Assert.AreEqual(0, keys.Count);
        }

        private GameState NewState()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            spawned.Add(config);
            return new GameState(config);
        }
    }
}
