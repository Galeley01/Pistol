// Нужен PerfOverlay.cs в веб-билде (WebGL): скачивание отчёта файлом и копирование в буфер обмена.
// Положить в Assets/Plugins/WebGL/PerfOverlay.jslib
mergeInto(LibraryManager.library, {
  PerfOverlay_SaveText: function (textPtr, fileNamePtr) {
    var text = UTF8ToString(textPtr);
    var name = UTF8ToString(fileNamePtr);

    // Буфер обмена — если браузер разрешит
    try {
      if (navigator.clipboard && navigator.clipboard.writeText)
        navigator.clipboard.writeText(text).catch(function () {});
    } catch (e) {}

    // Скачивание файлом — работает всегда
    try {
      var blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
      var url = URL.createObjectURL(blob);
      var a = document.createElement('a');
      a.href = url;
      a.download = name;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
    } catch (e) {
      console.log(text);
    }
  }
});
