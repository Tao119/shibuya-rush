mergeInto(LibraryManager.library, {
  WebAds_Enabled: function () { return (window.shibuyaAds && window.shibuyaAds.enabled) ? 1 : 0; },
  WebAds_Show: function (kind) { if (window.shibuyaAds) window.shibuyaAds.show(kind); }
});
