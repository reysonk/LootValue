using System;
using System.Linq;
using System.Reflection;
using Aki.Reflection.Patching;
using EFT.InventoryLogic;
using EFT.UI;
using static LootValue.TooltipUtils;

namespace LootValue
{

	internal class TooltipController
	{

		private static SimpleTooltip tooltip;

		public static void SetupTooltip(SimpleTooltip _tooltip, ref float delay)
		{
			tooltip = _tooltip;
			delay = 0;
		}

		public static void ClearTooltip()
		{
			tooltip?.Close();
			tooltip = null;
		}


		internal class ShowTooltipPatch : ModulePatch
		{

			protected override MethodBase GetTargetMethod()
			{
				return typeof(SimpleTooltip).GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(x => x.Name == "Show").ToList()[0];
			}

			[PatchPrefix]
			private static void Prefix(ref string text, ref float delay, SimpleTooltip __instance)
			{
				SetupTooltip(__instance, ref delay);

				var item = HoverItemController.hoveredItem;
				if (item == null || tooltip == null)
				{
					return;
				}

				bool pricesTooltipEnabled = LootValueMod.ShowPrices.Value;
				if (pricesTooltipEnabled == false)
				{
					return;
				}

				bool shouldShowPricesTooltipwhileInRaid = LootValueMod.ShowFleaPricesInRaid.Value;
				bool hideLowerPrice = LootValueMod.HideLowerPrice.Value;
				bool hideLowerPriceInRaid = LootValueMod.HideLowerPriceInRaid.Value;

				bool isInRaid = Globals.HasRaidStarted();

				if (!shouldShowPricesTooltipwhileInRaid && isInRaid)
				{
					return;
				}
				if (ItemUtils.ItemBelongsToTraderOrFleaMarketOrMail(item))
				{
					return;
				}

				var durability = ItemUtils.GetResourcePercentageOfItem(item);
				var missingDurability = 100 - durability * 100;

				int stackAmount = item.StackObjectsCount;
				bool isItemEmpty = item.IsEmpty();
				bool applyConditionReduction = LootValueMod.ReducePriceInFleaForBrokenItem.Value;

				int finalFleaPrice = FleaUtils.GetFleaMarketUnitPriceWithModifiers(item) * stackAmount;
				bool canBeSoldToFlea = item.MarkedAsSpawnedInSession && finalFleaPrice > 0;

				var finalTraderPrice = TraderUtils.GetBestTraderPrice(item);
				bool canBeSoldToTrader = finalTraderPrice > 0;

				// determine price per slot for each sale type				
				var size = item.CalculateCellSize();
				int slots = size.X * size.Y;

				int pricePerSlotTrader = finalTraderPrice / slots;
				int pricePerSlotFlea = finalFleaPrice / slots;

                bool isFleaPriceHigherThanTrader = finalFleaPrice > finalTraderPrice;
                bool isTraderPriceHigherThanFlea = finalTraderPrice > finalFleaPrice;
				bool sellToFlea = item.MarkedAsSpawnedInSession & item.Template.CanSellOnRagfair & isFleaPriceHigherThanTrader;
                bool sellToTrader = !sellToFlea;

                // If both trader and flea are 0, then the item is not purchasable.
                if (!canBeSoldToTrader && !canBeSoldToFlea)
				{
					AppendFullLineToTooltip(ref text, "[Предмет никто не покупает]", 11, "#AA3333");
					return;
				}

				var fleaPricesForWeaponMods = 0;
                var traderPricesForWeaponMods = 0;
                var shouldShowNonVitalModsPartsOfItem = LootValueMod.ShowNonVitalWeaponPartsFleaPrice.Value;
                
                if (shouldShowNonVitalModsPartsOfItem && ItemUtils.IsItemWeapon(item)) {

					var nonVitalMods = ItemUtils.GetWeaponNonVitalMods(item);
					fleaPricesForWeaponMods = FleaUtils.GetFleaValue(nonVitalMods);
                    traderPricesForWeaponMods = TraderUtils.GetTraderValue(nonVitalMods);
                }

                // TODO (maybe): add an option to use a modifier key to show the rest while in raid

                if (sellToFlea && TraderUtils.ShouldSellToTraderDueToPriceOrCondition(item) && !isInRaid)
				{
					isTraderPriceHigherThanFlea = true;
					isFleaPriceHigherThanTrader = false;
					sellToTrader = true;
					sellToFlea = false;

					var reason = GetReasonForItemToBeSoldToTrader(item);
					AppendFullLineToTooltip(ref text, $"[Следует продать <b>Торговцу {TraderUtils.GetBestTraderOffer(item).TraderName}</b> {reason}]", 11, "#AAAA33");
				}
                
                var showTraderPrice = true;
				if (hideLowerPrice && item.MarkedAsSpawnedInSession && isFleaPriceHigherThanTrader)
				{
					showTraderPrice = false;
				}
				if (hideLowerPriceInRaid && item.MarkedAsSpawnedInSession && isInRaid && isFleaPriceHigherThanTrader)
				{
					showTraderPrice = false;
				}
				if (finalTraderPrice == 0)
				{
					showTraderPrice = false;
				}

				if (canBeSoldToTrader || canBeSoldToFlea)
				{
					AppendSeparator(ref text, appendNewLineAfter: false);
				}

				// append trader price on tooltip
				if (showTraderPrice)
				{
					AppendNewLineToTooltipText(ref text);

                    // append trader price
                    //var traderName = $"Торговец: ";
                    var traderName = $"{TraderUtils.GetBestTraderOffer(item).TraderName}: ";
                    var traderNameColor = sellToTrader ? "#ffffff" : "#444444";
					var traderPricePerSlotColor = sellToTrader ? SlotColoring.GetColorFromValuePerSlots(pricePerSlotTrader) : "#444444";
					var fontSize = sellToTrader ? 14 : 12;

					StartSizeTag(ref text, fontSize);

					AppendTextToToolip(ref text, traderName, traderNameColor);
					AppendTextToToolip(ref text, $"₽ {finalTraderPrice.FormatNumber()}", traderPricePerSlotColor);

					if (stackAmount > 1)
					{
						var unitPrice = $" (₽ {(finalTraderPrice / stackAmount).FormatNumber()} шт)";
						AppendTextToToolip(ref text, unitPrice, "#333333");
					}

					EndSizeTag(ref text);

				}

				var showFleaPrice = true;
                if (!item.MarkedAsSpawnedInSession)
                {
                    showFleaPrice = false;
                }
                if (hideLowerPrice && item.MarkedAsSpawnedInSession && isTraderPriceHigherThanFlea)
				{
					showFleaPrice = false;
				}
				if (hideLowerPriceInRaid && item.MarkedAsSpawnedInSession && isInRaid && isTraderPriceHigherThanFlea)
				{
					showFleaPrice = false;
				}
				if (finalFleaPrice == 0)
				{
					showFleaPrice = false;
				}


				// append flea price on the tooltip
				if (showFleaPrice)
				{
					AppendNewLineToTooltipText(ref text);

					// append flea price
					var fleaName = $"Барахолка: ";
					var fleaNameColor = sellToFlea ? "#ffffff" : "#444444";
					var fleaPricePerSlotColor = sellToFlea ? SlotColoring.GetColorFromValuePerSlots(pricePerSlotFlea) : "#444444";
					var fontSize = sellToFlea ? 14 : 12;

					StartSizeTag(ref text, fontSize);

					AppendTextToToolip(ref text, fleaName, fleaNameColor);
					AppendTextToToolip(ref text, $"₽ {finalFleaPrice.FormatNumber()}", fleaPricePerSlotColor);

					if (applyConditionReduction)
					{
						if (missingDurability >= 1.0f)
						{
							var missingDurabilityText = $" (-{(int)missingDurability}%)";
							AppendTextToToolip(ref text, missingDurabilityText, "#AA1111");
						}
					}


					if (stackAmount > 1)
					{
						var unitPrice = $" (₽ {FleaUtils.GetFleaMarketUnitPriceWithModifiers(item).FormatNumber()} шт)";
						AppendTextToToolip(ref text, unitPrice, "#333333");
					}

					EndSizeTag(ref text);

					// Only show this out of raid
					if (!isInRaid && !isTraderPriceHigherThanFlea)
					{
						if (FleaUtils.ContainsNonFleableItemsInside(item))
						{
							AppendFullLineToTooltip(ref text, "[Содержит внутри предметы запрещенные на барахолке]", 11, "#AA3333");
							canBeSoldToFlea = false;
						}

					}

				}

				if(fleaPricesForWeaponMods > 0) {
					AppendNewLineToTooltipText(ref text);
					var color = SlotColoring.GetColorFromTotalValue(fleaPricesForWeaponMods);
					StartSizeTag(ref text, 12);
					AppendTextToToolip(ref text, $"₽ {fleaPricesForWeaponMods.FormatNumber()} ", color);
					AppendTextToToolip(ref text, $"стоимость обвеса (на барахолке)", "#555555");
					EndSizeTag(ref text);
				}

                if (traderPricesForWeaponMods > 0)
                {
                    AppendNewLineToTooltipText(ref text);
                    var color = SlotColoring.GetColorFromTotalValue(traderPricesForWeaponMods);
                    StartSizeTag(ref text, 12);
                    AppendTextToToolip(ref text, $"₽ {traderPricesForWeaponMods.FormatNumber()} ", color);
                    AppendTextToToolip(ref text, $"стоимость обвеса (у торговцев)", "#555555");
                    EndSizeTag(ref text);
                }
				                
                if (!isInRaid)
				{
					if (!isItemEmpty)
					{
						AppendFullLineToTooltip(ref text, "[Предмет не пуст]", 11, "#AA3333");
						canBeSoldToFlea = false;
						canBeSoldToTrader = false;
					}
				}

				var shouldShowFleaMarketEligibility = LootValueMod.ShowFleaMarketEligibility.Value;
				if (shouldShowFleaMarketEligibility && !item.Template.CanSellOnRagfair)
				{
					AppendFullLineToTooltip(ref text, "[Предмет запрещен к продаже на барахолке]", 11, "#AA3333");
				}

				var shouldShowPricePerSlotAndPerKgInRaid = LootValueMod.ShowPricePerKgAndPerSlotInRaid.Value;
				if (isInRaid && shouldShowPricePerSlotAndPerKgInRaid)
				{

					var pricePerSlot = sellToTrader ? pricePerSlotTrader : pricePerSlotFlea;
					var unitPrice = sellToTrader ? (finalTraderPrice / stackAmount) : FleaUtils.GetFleaMarketUnitPriceWithModifiers(item);
					var pricePerWeight = (int)(unitPrice / item.GetSingleItemTotalWeight());


                    AppendSeparator(ref text);
					StartSizeTag(ref text, 12);
					if (pricePerWeight > 0) {
                        AppendTextToToolip(ref text, $"₽/КГ: {pricePerWeight.FormatNumber()}", "#555555");
                        AppendNewLineToTooltipText(ref text);
                    }
					AppendTextToToolip(ref text, $"₽/СЛОТ: {pricePerSlot.FormatNumber()}", "#555555");
					EndSizeTag(ref text);

				}


				bool quickSellEnabled = LootValueMod.EnableQuickSell.Value;
                bool quickSellEnabledHint = LootValueMod.EnableQuickSellHint.Value;
                bool quickSellUsesOneButton = LootValueMod.OneButtonQuickSell.Value;
                bool showQuickSaleCommands = quickSellEnabled && quickSellEnabledHint && !isInRaid;
                bool showQuickSaleCommandsadds = quickSellEnabled && !quickSellEnabledHint && !isInRaid;

                if (showQuickSaleCommands)
				{
					if (quickSellUsesOneButton)
					{

						bool canBeSold = (sellToFlea && canBeSoldToFlea) ||
														 (sellToTrader && canBeSoldToTrader);

						if (canBeSold)
						{
							AppendSeparator(ref text);
							AppendTextToToolip(ref text, $"Продать с помощью Alt+Shift+Click", "#888888");
							if (canBeSoldToFlea && sellToFlea)
							{
								AddMultipleItemsSaleSection(ref text, item);
							}
						}

					}
					else
					{
						if (canBeSoldToFlea || canBeSoldToTrader)
						{
							AppendSeparator(ref text);
						}

						if (canBeSoldToTrader)
						{
							AppendTextToToolip(ref text, $"Продать трейдеру с помощью Alt+Shift+Left Click", "#888888");
						}

						if (canBeSoldToFlea && canBeSoldToTrader)
						{
							AppendNewLineToTooltipText(ref text);
						}

						if (canBeSoldToFlea)
						{
							AppendTextToToolip(ref text, $"Продать на барахолке с помощью Alt+Shift+Right Click", "#888888");
							AddMultipleItemsSaleSection(ref text, item);
						}
					}

				}

				if (showQuickSaleCommandsadds)
				{
                    if (canBeSoldToFlea)
                    {
                        AddMultipleItemsSaleSection(ref text, item);
                    }
                }


            }

			private static void AddMultipleItemsSaleSection(ref string text, Item item)
			{
				bool canSellSimilarItems = FleaUtils.CanSellMultipleOfItem(item);
				if (canSellSimilarItems)
				{
					// append only if more than 1 item will be sold due to the flea market action
					var amountOfItems = ItemUtils.CountItemsSimilarToItemWithinSameContainer(item);
					if (amountOfItems > 1)
					{
						var totalPrice = FleaUtils.GetTotalPriceOfAllSimilarItemsWithinSameContainer(item);
						AppendFullLineToTooltip(ref text, $"[Будет продано {amountOfItems} аналогичных предметов за ₽ {totalPrice.FormatNumber()}]", 10, "#555555");
					}

				}
			}

			private static string GetReasonForItemToBeSoldToTrader(Item item)
			{
				var flags = DurabilityOrProfitConditionFlags.GetDurabilityOrProfitConditionFlagsForItem(item);
				if (flags.shouldSellToTraderDueToBeingNonOperational)
				{
					return "из-за того, что предмет не работает";
				}
				else if (flags.shouldSellToTraderDueToDurabilityThreshold)
				{
					return "из-за низкой прочности";
				}
				else if (flags.shouldSellToTraderDueToProfitThreshold)
				{
					return "из-за низкой прибыли от продажи на барахолке";
				}
				return "без всякой причины :)";
			}

		}



	}

}