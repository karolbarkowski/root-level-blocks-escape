using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;

namespace BlocksEscape
{
    // The one in-app purchase: unlock_all_levels, a non-consumable Google Play product that opens every section after
    // the first (Unity IAP v5; the editor gets Unity's fake store). The store connects at startup and its owned
    // purchases come back on their own, so a reinstall or another device restores the unlock without a button.
    //
    // The unlock is cached in PlayerPrefs and only ever set, never cleared by the store: a player who once had it keeps
    // playing offline or when Play is unreachable. Changed is raised whenever IsUnlocked, IsPending, Price or
    // the purchase state changes.
    public static class Entitlements
    {
        public const string ProductId = "unlock_all_levels";
        const string UnlockedKey = "entitlements.unlockAll";

        public enum PurchaseResult { None, Cancelled, Failed, Unavailable }

        public static event Action Changed;

        public static bool IsUnlocked => PlayerPrefs.GetInt(UnlockedKey, 0) != 0;

        // A purchase Play has accepted but not yet been paid for (e.g. cash at a shop); it unlocks once paid.
        public static bool IsPending { get; private set; }

        // A purchase flow is open (the Play dialog is showing).
        public static bool IsPurchasing { get; private set; }

        // How the last purchase attempt ended, for the unlock screen; None until one ends without unlocking.
        public static PurchaseResult LastResult { get; private set; }

        // The localised price from Play, or null while the product is unknown (offline, store not ready).
        public static string Price { get; private set; }

        public static bool CanBuy => _store != null && _product != null && !IsPurchasing && !IsPending && !IsUnlocked;

        static StoreController _store;
        static Product _product;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // Static state survives play mode restarts in the editor without a domain reload.
            _store = null;
            _product = null;
            IsPending = IsPurchasing = false;
            LastResult = PurchaseResult.None;
            Price = null;
            Connect();
        }

        static async void Connect()
        {
            try
            {
                var store = UnityIAPServices.StoreController();
                // Every handler goes on before Connect: owned and unfinished purchases may arrive straight away.
                store.OnPurchasePending += OnPurchasePending;
                store.OnPurchaseConfirmed += OnPurchaseConfirmed;
                store.OnPurchaseFailed += OnPurchaseFailed;
                store.OnPurchaseDeferred += OnPurchaseDeferred;
                store.OnPurchasesFetched += OnPurchasesFetched;
                store.OnPurchasesFetchFailed += failure => Debug.LogWarning($"IAP: purchases fetch failed: {failure.Message}");
                store.OnProductsFetched += OnProductsFetched;
                store.OnProductsFetchFailed += failure => Debug.LogWarning($"IAP: product fetch failed: {failure.FailureReason}");
                store.OnStoreConnected += () => OnConnected(store);
                store.OnStoreDisconnected += failure => Debug.LogWarning($"IAP: store disconnected: {failure.Message}");
                store.OnAuthAccountChanged += () => OnConnected(store); // products and purchases were cleared: fetch again
                _store = store;
                await store.Connect();
            }
            catch (Exception e)
            {
                // No store (offline, no Play services): the cached unlock still applies.
                Debug.LogWarning($"IAP: store unavailable: {e.Message}");
            }
        }

        static void OnConnected(StoreController store)
        {
            store.FetchProducts(new List<ProductDefinition> { new(ProductId, ProductType.NonConsumable) });
        }

        static void OnProductsFetched(List<Product> products)
        {
            _product = products.FirstOrDefault(IsOurs) ?? _product;
            string price = _product?.metadata?.localizedPriceString;
            Price = string.IsNullOrEmpty(price) ? null : price;
            // Purchases are fetched once the product is known, so their carts name it.
            _store?.FetchPurchases();
            Changed?.Invoke();
        }

        static void OnPurchasesFetched(Orders orders)
        {
            // Pending orders also come through OnPurchasePending, which confirms them.
            if (orders.ConfirmedOrders.Any(IsOurs)) Unlock();
            bool pending = orders.DeferredOrders.Any(IsOurs);
            if (pending != IsPending)
            {
                IsPending = pending;
                Changed?.Invoke();
            }
        }

        // Starts the Play purchase dialog. The result arrives through Changed.
        public static void Buy()
        {
            if (!CanBuy)
            {
                if (!IsUnlocked && !IsPending && !IsPurchasing) End(PurchaseResult.Unavailable);
                return;
            }
            IsPurchasing = true;
            LastResult = PurchaseResult.None;
            Changed?.Invoke();
            _store.PurchaseProduct(_product);
        }

        // Paid for (now or earlier): grant, then let Play finish (acknowledge) the order.
        static void OnPurchasePending(PendingOrder order)
        {
            if (!IsOurs(order)) return;
            Unlock();
            _store.ConfirmPurchase(order);
        }

        static void OnPurchaseConfirmed(Order order)
        {
            if (!IsOurs(order)) return;
            if (order is FailedOrder failed)
                Debug.LogWarning($"IAP: confirmation failed: {failed.FailureReason} {failed.Details}");
            // Already unlocked when it was pending; a failed acknowledgement is retried by the store on the next start.
            Unlock();
        }

        static void OnPurchaseFailed(FailedOrder order)
        {
            if (!IsOurs(order)) return;
            switch (order.FailureReason)
            {
                case PurchaseFailureReason.UserCancelled:
                    End(PurchaseResult.Cancelled);
                    break;
                case PurchaseFailureReason.DuplicateTransaction:
                    // Already owned: Play said so instead of selling it again.
                    Unlock();
                    break;
                default:
                    Debug.LogWarning($"IAP: purchase failed: {order.FailureReason} {order.Details}");
                    End(PurchaseResult.Failed);
                    break;
            }
        }

        static void OnPurchaseDeferred(DeferredOrder order)
        {
            if (!IsOurs(order)) return;
            IsPending = true;
            IsPurchasing = false;
            Changed?.Invoke();
        }

        static void Unlock()
        {
            bool changed = !IsUnlocked || IsPending || IsPurchasing;
            if (!IsUnlocked)
            {
                PlayerPrefs.SetInt(UnlockedKey, 1);
                PlayerPrefs.Save();
            }
            IsPending = IsPurchasing = false;
            LastResult = PurchaseResult.None;
            if (changed) Changed?.Invoke();
        }

        static void End(PurchaseResult result)
        {
            IsPurchasing = false;
            LastResult = result;
            Changed?.Invoke();
        }

        static bool IsOurs(Product product) => product?.definition?.id == ProductId;

        static bool IsOurs(Order order) =>
            order?.CartOrdered?.Items()?.Any(item => IsOurs(item.Product)) ?? false;

#if UNITY_EDITOR
        // Forgets the cached unlock (testing only; the store gives it back on the next start if it is owned).
        public static void ResetCache()
        {
            PlayerPrefs.DeleteKey(UnlockedKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
#endif
    }
}
