mergeInto(LibraryManager.library, {
  // スマホ・タブレットのブラウザなら 1、PC なら 0(Unity の Input.touchSupported は WebGL ではPCでも true になりやすいので、ブラウザ側で判定する)
  WebEnv_IsTouch: function () {
    var coarse = window.matchMedia && window.matchMedia("(pointer: coarse)").matches;
    var mobileUa = /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent);
    var ipadOs = navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1;
    return (coarse || mobileUa || ipadOs) ? 1 : 0;
  },
  // ゲーム集約ページ(ポータル)へ戻る
  WebEnv_GoPortal: function () {
    window.location.href = "/games";
  }
});
