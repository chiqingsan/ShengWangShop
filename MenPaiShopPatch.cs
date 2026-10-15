using System;
using System.Collections.Generic;
using System.Text;
using GUIPackage;
using HarmonyLib;
using JSONClass;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShengWangShop;

/// <summary>
/// 接管门派商店面板（UIMenPaiShop.RefreshUI），处理两类"用灵石支付"的场景：
///
/// ① 五大门派库房换算（需宁州声望达标，默认 ≥ 6 声名远扬）：
///    支付货币由本门贡献货币（沧元丹/元灵石/寒玉/火晶/砂晶）改为灵石，
///    价格按原版隐含换算 1 贡献 = 100 灵石（即物品底价）。
///    例：一袋银月花 = 18 沧元丹 = 1800 灵石。
///
/// ② 原生以灵石定价的商店（第三方 mod 新增，如"碎星商会库房"）：
///    修复原版面板的限制——原版按"背包物品"查询货币数量（getItemNum），
///    而灵石存在 Player.money 钱包字段、不是背包物品，导致这类商店经场景
///    按钮打开时货币栏显示 0、且无法购买。本补丁识别后用灵石正常计价与
///    购买，价格保持商店数据原定义（不做换算）。
///
/// 换算范围严格限定为五大门派库房（按计价货币判断）；其他共用该面板的商店
/// （藏宝阁、藏经阁/秘阁、神兵阁，以及第三方库房如白帝楼库房"剑玉"等）
/// 一律保持原版行为。声望未达标时五大门派库房同样保持原版（贡献货币）。
///
/// 渲染流程复刻原版 UIMenPaiShop.RefreshUI，仅支付路径不同：
///   原版  Player.getItemNum(货币) / removeItem(货币, 总价)
///   本版  (int)Player.money     / Player.AddMoney(-总价)
/// （与游戏自带兑换商店 UIDuiHuanShop 对 EXGoodsID == 10035 的官方分支一致。）
/// </summary>
[HarmonyPatch(typeof(UIMenPaiShop), "RefreshUI")]
public static class MenPaiShopPatch
{
	/// <summary>灵石物品 ID（配表 d_items.py.items.json，type 7 货币类；实际数量存于 Player.money 字段）。</summary>
	private const int LingShiItemId = 10035;

	/// <summary>1 点门派贡献货币折合的灵石数（五大门派库房 percent=100，物品底价的 1% 用贡献支付）。</summary>
	private const int LingShiPerContribution = 100;

	/// <summary>
	/// 五大门派贡献货币：沧元丹(竹山宗 10010) / 元灵石(金虹剑派 10011) / 寒玉(星河剑派 10012)
	/// / 火晶(离火门 10013) / 砂晶(化尘教 10014)。只有这些货币的库房才做贡献价换算，
	/// 用于排除第三方 mod 新增的同面板商店（如白帝楼库房的"剑玉"）。
	/// </summary>
	private static readonly HashSet<int> SectContributionCurrencies = new HashSet<int> { 10010, 10011, 10012, 10013, 10014 };

	/// <summary>
	/// 玩家持有灵石数。money 是 ulong 字段，但游戏自身（AddMoney 等）按 int 域运算，
	/// 超过 int.MaxValue 时 (int) 截断为负数会破坏一切比较，这里统一封顶读取。
	/// </summary>
	private static int GetPlayerMoneyInt()
	{
		ulong money = PlayerEx.Player.money;
		return money > (ulong)int.MaxValue ? int.MaxValue : (int)money;
	}

	/// <summary>
	/// 打开药房时使用的门派子场景覆盖（如 "S125"）。正常在门派场景内打开药房时为 null，
	/// 由大地图"购买药材"选项设置，使 RefreshUI 能在 AllMaps 场景下渲染指定门派的库房。
	/// </summary>
	public static string OverrideShopScene;

	/// <summary>在大地图上打开指定门派的药房购买面板（灵石支付）。</summary>
	public static void OpenSectShop(string shopScene)
	{
		if ((UnityEngine.Object)UIMenPaiShop.Inst == (UnityEngine.Object)null)
		{
			Plugin.Log.LogError("[声望药房] UIMenPaiShop 实例不存在，无法打开药房面板");
			return;
		}
		OverrideShopScene = shopScene;
		UIMenPaiShop.Inst.Show();
		UIMenPaiShop.Inst.RefreshUI();
	}

	public static bool Prefix(UIMenPaiShop __instance)
	{
		if (!Plugin.Config_Enabled.Value)
		{
			_renderedShopKey = null;
			return true;
		}
		try
		{
			string sceneName = string.IsNullOrEmpty(OverrideShopScene) ? SceneEx.NowSceneName : OverrideShopScene;
			List<NomelShopJsonData> list = NomelShopJsonData.DataList.FindAll((NomelShopJsonData d) => $"S{d.threeScene}" == sceneName);
			if (list.Count == 0)
			{
				// 此场景无商店数据，交回原版处理
				_renderedShopKey = null;
				return true;
			}
			int pageCount = Mathf.Min(3, list.Count);
			int currency = GetUniformCurrency(list, pageCount);
			if (list[0].SType == 3 && SectContributionCurrencies.Contains(currency))
			{
				// ① 五大门派库房：声望未达标时保持原版（贡献货币）
				if (PlayerEx.GetNingZhouShengWangLevel() < Plugin.Config_MinShengWangLevel.Value)
				{
					_renderedShopKey = null;
					return true;
				}
				RefreshShopWithLingShi(__instance, sceneName, list, LingShiPerContribution);
			}
			else if (currency == LingShiItemId)
			{
				// ② 原生灵石计价商店：不改价格，仅补齐原版缺失的灵石钱包支持
				RefreshShopWithLingShi(__instance, sceneName, list, 1);
			}
			else
			{
				// 其他商店（藏宝阁/藏经阁/秘阁/神兵阁/第三方非贡献货币库房等）：保持原版
				_renderedShopKey = null;
				return true;
			}
		}
		catch (Exception e)
		{
			Plugin.Log.LogError("灵石商店渲染失败，回退原版逻辑：" + e);
			_renderedShopKey = null;
			return true;
		}
		return false;
	}

	/// <summary>
	/// 商店的计价货币（EXGoodsID）。面板按单一货币渲染（顶部只显示一种货币），
	/// 因此要求本场景各页商品货币一致；有任何不一致或商品为空时返回 0，
	/// 交由原版处理（例如灵界"蜀山剑派藏宝阁"多页货币不同的商店）。
	/// </summary>
	private static int GetUniformCurrency(List<NomelShopJsonData> list, int pageCount)
	{
		int currency = 0;
		for (int i = 0; i < pageCount; i++)
		{
			List<jiaoHuanShopGoods> goods = UIDuiHuanShop.GetShopGoods(list[i].ExShopID);
			if (goods.Count == 0)
			{
				return 0;
			}
			foreach (jiaoHuanShopGoods g in goods)
			{
				if (currency == 0)
				{
					currency = g.EXGoodsID;
				}
				else if (currency != g.EXGoodsID)
				{
					return 0;
				}
			}
		}
		return currency;
	}

	/// <summary>
	/// 上次渲染的"计价模式 + 场景 + 商品集合"标识。与当前一致且面板已有内容时跳过 UI 重建，
	/// 只刷新灵石数字（重建约 30 个商品对象（Instantiate + Destroy）是打开面板卡顿的主要来源；
	/// 商店商品固定、无库存概念，可安全复用）。
	/// </summary>
	private static string _renderedShopKey;

	/// <summary>
	/// 渲染商店面板。priceMultiplier：五大门派库房为 LingShiPerContribution（贡献价 ×100 换算），
	/// 原生灵石商店为 1（价格保持商店数据原定义）。
	/// </summary>
	private static void RefreshShopWithLingShi(UIMenPaiShop shop, string sceneName, List<NomelShopJsonData> list, int priceMultiplier)
	{
		// 原版硬编码 3 页，这里防御数据不足 3 条的场景
		int pageCount = Mathf.Min(3, list.Count);
		List<List<jiaoHuanShopGoods>> pages = new List<List<jiaoHuanShopGoods>>(pageCount);
		StringBuilder keyBuilder = new StringBuilder(64);
		keyBuilder.Append(priceMultiplier).Append('|').Append(sceneName);
		for (int i = 0; i < pageCount; i++)
		{
			List<jiaoHuanShopGoods> goods = UIDuiHuanShop.GetShopGoods(list[i].ExShopID);
			goods.Sort();
			pages.Add(goods);
			foreach (jiaoHuanShopGoods g in goods)
			{
				keyBuilder.Append('|').Append(g.GoodsID);
			}
		}
		string shopKey = keyBuilder.ToString();
		bool hasContent = false;
		for (int i = 0; i < pageCount; i++)
		{
			if (shop.ShopRT[i].childCount > 0)
			{
				hasContent = true;
				break;
			}
		}
		if (shopKey == _renderedShopKey && hasContent)
		{
			shop.MoneyText.text = GetPlayerMoneyInt().ToString();
			return;
		}
		shop.ShopTitle.text = list[0].Title;
		int levelType = PlayerEx.Player.getLevelType();
		ItemDatebase itemDb = jsonData.instance.GetComponent<ItemDatebase>();
		for (int i = 0; i < pageCount; i++)
		{
			NomelShopJsonData page = list[i];
			shop.ShopName[i].text = page.ChildTitle;
			List<jiaoHuanShopGoods> shopGoods = pages[i];
			((Transform)(object)shop.ShopRT[i]).DestoryAllChild();
			foreach (jiaoHuanShopGoods good in shopGoods)
			{
				_ItemJsonData item = _ItemJsonData.DataDict[good.GoodsID];
				if (page.SType == 1 && levelType < item.quality && (item.type == 3 || item.type == 4))
				{
					continue;
				}
				UIMenPaiShopItem component = UnityEngine.Object.Instantiate<GameObject>(shop.UIMenPaiShopItemPrefab, (Transform)(object)shop.ShopRT[i]).GetComponent<UIMenPaiShopItem>();
				int price = CalcLingShiPrice(item, good, priceMultiplier);
				component.PriceText.text = price.ToString();
				component.PriceIcon.sprite = itemDb.items[LingShiItemId].itemIconSprite;
				component.IconShow.SetItem(good.GoodsID);
				component.IconShow.Count = 1;
				int goodsId = good.GoodsID;
				component.IconShow.OnClick += (UnityAction<PointerEventData>)delegate
				{
					int maxBuy = Mathf.Min(GetPlayerMoneyInt() / price, item.maxNum);
					// 游戏获得物品是逐件（袋装药材还会逐袋拆开）处理，单次数量必须设上限，
					// 否则大量购买会卡死游戏
					maxBuy = Mathf.Min(maxBuy, Plugin.Config_MaxBuyPerAction.Value);
					switch (maxBuy)
					{
					case 0:
						UIPopTip.Inst.Pop("灵石不足");
						break;
					case 1:
						USelectBox.Show("是否购买" + item.name + " x1", (UnityAction)delegate
						{
							BuyItem(goodsId, price, 1);
						});
						break;
					default:
						// 注意：USelectNum 的标题用 Decs.Replace("{num}", 当前值) 更新，
						// 必须保留 {num} 占位符，否则拖动滑块时显示的数量不会变化
						USelectNum.Show("购买数量 x{num}", 1, maxBuy, (UnityAction<int>)delegate(int num)
						{
							BuyItem(goodsId, price, num);
						});
						break;
					}
				};
			}
		}
		shop.MoneyIcon.sprite = itemDb.items[LingShiItemId].itemIconSprite;
		shop.MoneyText.text = GetPlayerMoneyInt().ToString();
		_renderedShopKey = shopKey;
	}

	/// <summary>
	/// 灵石价 = ceil(物品底价 / percent) × priceMultiplier。
	/// 五大门派库房：percent=100、multiplier=100 → 贡献价 ×100（1 贡献 = 100 灵石）；
	/// 原生灵石商店：multiplier=1 → 商店数据定义的价格。
	/// percent &lt;= 0 时（配表异常的防御）直接按物品底价计。
	/// </summary>
	private static int CalcLingShiPrice(_ItemJsonData item, jiaoHuanShopGoods good, int priceMultiplier)
	{
		if (good.percent <= 0)
		{
			return item.price;
		}
		int basePrice = item.price / good.percent;
		if (item.price % good.percent > 0)
		{
			basePrice++;
		}
		return basePrice * priceMultiplier;
	}

	private static void BuyItem(int goodsId, int price, int num)
	{
		// long 计算防用户调大 MaxBuyPerAction 后 price * num 溢出 int
		long need = (long)price * num;
		if (GetPlayerMoneyInt() < need)
		{
			UIPopTip.Inst.Pop("灵石不足");
			return;
		}
		PlayerEx.Player.AddMoney(-(int)need);
		PlayerEx.Player.addItem(goodsId, num, Tools.CreateItemSeid(goodsId));
		// 增量刷新：商店商品固定且无库存概念，购买后只需更新顶部灵石数字；
		// 不调用 UIMenPaiShop.Inst.RefreshUI()（会销毁重建整个面板，造成每次购买的卡顿感）
		if ((UnityEngine.Object)UIMenPaiShop.Inst != (UnityEngine.Object)null && (UnityEngine.Object)UIMenPaiShop.Inst.MoneyText != (UnityEngine.Object)null)
		{
			UIMenPaiShop.Inst.MoneyText.text = GetPlayerMoneyInt().ToString();
		}
		UIPopTip.Inst.Pop("购买了" + _ItemJsonData.DataDict[goodsId].name + "x" + num, PopTipIconType.包裹);
	}
}

/// <summary>药房面板关闭时清除场景覆盖，恢复正常按当前场景渲染。</summary>
[HarmonyPatch(typeof(UIMenPaiShop), "Close")]
public static class MenPaiShopClosePatch
{
	public static void Postfix()
	{
		MenPaiShopPatch.OverrideShopScene = null;
	}
}
