using SaintCoinach.Ex.Relational;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaintCoinach.Xiv {
    public class DisposalShopItem : XivSubRow, IShopListing, IShopListingItem {

        #region Fields

        /// <summary>
        ///     Cost of the current shop item.
        /// </summary>
        private readonly ShopListingItem _Cost;

        /// <summary>
        ///     Shops offering the current item.
        /// </summary>
        private DisposalShop _DisposalShop;

        #endregion

        #region Properties

        /// <summary>
        ///     Gets the associated <see cref="Item" />.
        /// </summary>
        /// <value>The associated <see cref="Item" />.</value>
        public Item Item { get { return As<Item>("Item{Received}"); } }

        #endregion

        #region Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="ShopItem" /> class.
        /// </summary>
        /// <param name="sheet"><see cref="IXivSheet" /> containing this object.</param>
        /// <param name="sourceRow"><see cref="IRelationalRow" /> to read data from.</param>
        public DisposalShopItem(IXivSheet sheet, IRelationalRow sourceRow) : base(sheet, sourceRow) {
            _Cost = new ShopListingItem(this, As<Item>("Item{Disposed}"), 1, AsBoolean("HQ{Disposed}"), 0);
        }

        #endregion

        /// <summary>
        ///     Returns a string representation of the current shop item.
        /// </summary>
        /// <returns>The name of <see cref="Item" />.</returns>
        public override string ToString() {
            return string.Format("{0}", Item);
        }

        #region IShopListing Members

        /// <summary>
        ///     Gets the rewards of the current listing.
        /// </summary>
        /// <value>The rewards of the current listing.</value>
        IEnumerable<IShopListingItem> IShopListing.Rewards { get { yield return this; } }

        /// <summary>
        ///     Gets the costs of the current listing.
        /// </summary>
        /// <value>The costs of the current listing.</value>
        IEnumerable<IShopListingItem> IShopListing.Costs { get { yield return _Cost; } }

        /// <summary>
        ///     Gets the shops offering the current listing.
        /// </summary>
        /// <value>The shops offering the current listing.</value>
        public IEnumerable<IShop> Shops { get { yield return _DisposalShop; } }

        #endregion

        #region IShopListingItem Members

        /// <summary>
        ///     Gets the item of the current listing entry.
        /// </summary>
        /// <value>The item of the current listing entry.</value>
        Item IShopListingItem.Item { get { return Item; } }

        /// <summary>
        ///     Gets the count for the current listing entry.
        /// </summary>
        /// <value>
        ///     <value>1</value>
        /// </value>
        int IShopListingItem.Count { get { return AsInt32("Quantity{Received}"); } }

        /// <summary>
        ///     Gets a value indicating whether the item is high-quality.
        /// </summary>
        /// <value>
        ///     <c>false</c>
        /// </value>
        bool IShopListingItem.IsHq { get { return false; } }

        /// <summary>
        ///     Gets the collectability rating for the item.
        /// </summary>
        /// <value>
        ///     <c>false</c>
        /// </value>
        int IShopListingItem.CollectabilityRating { get { return 0; } }

        /// <summary>
        ///     Gets the <see cref="IShopListing" /> the current entry is for.
        /// </summary>
        /// <value>
        ///     <c>this</c>
        /// </value>
        IShopListing IShopListingItem.ShopItem { get { return this; } }

        #endregion
    }
}
