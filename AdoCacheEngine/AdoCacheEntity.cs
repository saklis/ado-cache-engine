namespace AdoCache {
    /// <summary>
    ///     Base class for cashe model items.
    /// </summary>
    public abstract class AdoCacheEntity {
        public AdoCacheEntity() {
            _isManagedByCacheEngine = false;
        }
        public AdoCacheEntity(bool isManagedByCacheEngine) {
            _isManagedByCacheEngine = isManagedByCacheEngine;
        }

        protected bool _isManagedByCacheEngine = false;

        public bool IsManagedByCacheEngine => _isManagedByCacheEngine;

        protected abstract void CopyNewValues();

        public abstract void UndoPendingChanges();
    }
}