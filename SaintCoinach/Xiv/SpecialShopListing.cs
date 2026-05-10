using SaintCoinach.Libra;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SaintCoinach.Xiv {
    /// <summary>
    ///     Class representing a listing in a <see cref="SpecialShop" />.
    /// </summary>
    public class SpecialShopListing : IShopListing {
        #region Currency

        private static Dictionary<int, int> _Currencies = new Dictionary<int, int>() {
            { 1, 28 },
            { 2, 33913 },
            { 4, 33914 },
            { 6, 41784 },
            { 7, 41785 }
        };

        private static Dictionary<int, int> _Tomestones;

        private void BuildTomestones() {
            // Tomestone currencies rotate across patches.
            // These keys correspond to currencies A, B, and C.
            var sTomestonesItems = SpecialShop.Sheet.Collection.GetSheet<TomestonesItem>()
                .Where(t => t.Tomestone.Key > 0)
                .OrderBy(t => t.Tomestone.Key)
                .ToArray();

            _Tomestones = new Dictionary<int, int>();

            for (int i = 0; i < sTomestonesItems.Length; i++) {
                _Tomestones[i + 1] = sTomestonesItems[i].Item.Key;
            }
        }

        private static int GetCurrency(int key) {
            if (_Currencies.ContainsKey(key)) {
                return _Currencies[key];
            }
            return key;
        }

        private static int GetTomestoneCoveredCurrency(int key) {
            if (key <= 3) {
                return _Tomestones[key];
            }
            else {
                return GetCurrency(key);
            }
        }

        #endregion

        #region Fields

        /// <summary>
        ///     Costs of the current listing.
        /// </summary>
        private readonly ShopListingItem[] _Costs;

        /// <summary>
        ///     Rewards of the current listing.
        /// </summary>
        private readonly ShopListingItem[] _Rewards;

        #endregion

        #region Properties

        /// <summary>
        ///     Gets the <see cref="SpecialShop" /> the current listing is from.
        /// </summary>
        /// <value>The <see cref="SpecialShop" /> the current listing is from.</value>
        public SpecialShop SpecialShop { get; private set; }

        /// <summary>
        ///     Gets the <see cref="Quest" /> required for the current listing.
        /// </summary>
        /// <value>The <see cref="Quest" /> required for the current listing.</value>
        public Quest Quest { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="SpecialShopListing" /> class.
        /// </summary>
        /// <param name="shop"><see cref="SpecialShop" /> for which the listing is.</param>
        /// <param name="index">Position of the listing in the <c>shop</c>'s data.</param>
        public SpecialShopListing(SpecialShop shop, int index) {
            SpecialShop = shop;

            const int RewardCount = 2;
            var rewards = new List<ShopListingItem>();
            for (var i = 0; i < RewardCount; ++i) {
                var item = shop.As<Item>("Item{Receive}", index, i);
                if (item.Key == 0)
                    continue;

                var count = shop.AsInt32("Count{Receive}", index, i);
                if (count == 0)
                    continue;

                var hq = shop.AsBoolean("HQ{Receive}", index, i);

                rewards.Add(new ShopListingItem(this, item, count, hq, 0));
            }
            _Rewards = rewards.ToArray();
            Quest = shop.As<Quest>("Quest{Item}", index);

            int UseCurrencyType = shop.As<byte>("UseCurrencyType");

            const int CostCount = 3;
            var costs = new List<ShopListingItem>();
            for (var i = 0; i < CostCount; ++i) {
                var item = shop.As<Item>("Item{Cost}", index, i);

                if (item.Key == 0)
                    continue;

                var count = shop.AsInt32("Count{Cost}", index, i);
                if (count == 0)
                    continue;

                var hq = shop.AsBoolean("HQ{Cost}", index, i);

                int res = item.Key;
                if (item.Key < 8) {
                    switch (UseCurrencyType) {
                        case 16:
                            res = _Currencies[item.Key];
                            break;
                        case 8:
                            res = 1;
                            break;
                        case 4:
                        case 2:
                            if (_Tomestones == null) {
                                BuildTomestones();
                            }
                            res = _Tomestones[item.Key];
                            break;
                    }
                }

                if (SpecialShop.Key == 1770637 || SpecialShop.Key == 1770310) {
                    res = GetCurrency(item.Key);
                }
                else if (SpecialShop.Key == 1770446 || SpecialShop.Key == 1769500 || (SpecialShop.Key == 1770699 && item.Key < 10)) {
                    res = GetTomestoneCoveredCurrency(item.Key);
                }
                else {
                    if (UseCurrencyType == 16 && item.Key != 25) {
                        res = GetCurrency(item.Key);
                    }

                    if ((UseCurrencyType == 2) && item.Key < 10) {
                        res = GetTomestoneCoveredCurrency(item.Key);
                    }
                }

                if (res != item.Key) {
                    hq = false;
                }
                item = shop.Sheet.Collection.GetSheet<Item>()[res];


                var collectabilityRating = shop.AsInt16("CollectabilityRating{Cost}", index, i);

                costs.Add(new ShopListingItem(this, item, count, hq, collectabilityRating));
            }
            _Costs = costs.ToArray();
        }

        #endregion

        /// <summary>
        ///     Gets the rewards of the current listing.
        /// </summary>
        /// <value>The rewards of the current listing.</value>
        public IEnumerable<IShopListingItem> Rewards { get { return _Rewards; } }

        /// <summary>
        ///     Gets the costs of the current listing.
        /// </summary>
        /// <value>The costs of the current listing.</value>
        public IEnumerable<IShopListingItem> Costs { get { return _Costs; } }

        #region IShopItem Members

        /// <summary>
        ///     Gets the shops offering the current listing.
        /// </summary>
        /// <value>The shops offering the current listing.</value>
        IEnumerable<IShop> IShopListing.Shops { get { yield return SpecialShop; } }

        #endregion
    }
}
