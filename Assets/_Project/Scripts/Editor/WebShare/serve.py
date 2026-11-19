# Local web server for a Unity WebGL build, started by Pistol > Web Share (WebShareProcesses.cs).
# Usage: python3 serve.py <build folder> <port>
# - listens on 127.0.0.1 only (the Cloudflare tunnel connects to it locally);
# - sends COOP/COEP headers: multithreaded WebGL builds need them (SharedArrayBuffer);
# - serves Unity's pre-compressed files (name.wasm.gz / .br) with Content-Encoding and the right type;
# - no directory listings, no hidden files, no caching, no console log.
import mimetypes
import os
import sys
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

mimetypes.add_type("application/wasm", ".wasm")
mimetypes.add_type("application/javascript", ".js")
ENCODINGS = {".gz": "gzip", ".br": "br"}


class Handler(SimpleHTTPRequestHandler):
    def _clean_path(self):
        return self.path.split("?", 1)[0].split("#", 1)[0]

    def send_head(self):
        parts = [p for p in self._clean_path().split("/") if p]
        if any(p.startswith(".") for p in parts):   # hidden files and "..": not served
            self.send_error(404)
            return None
        if os.path.isdir(self.translate_path(self.path)) and not os.path.isfile(
                os.path.join(self.translate_path(self.path), "index.html")):
            self.send_error(404)                      # no directory listings
            return None
        return super().send_head()

    def guess_type(self, path):
        base, ext = os.path.splitext(path)
        if ext.lower() in ENCODINGS:
            path = base                               # Build.wasm.gz -> type of .wasm
        return mimetypes.guess_type(path)[0] or "application/octet-stream"

    def send_response(self, code, message=None):
        self._code = code
        super().send_response(code, message)

    def end_headers(self):
        ext = os.path.splitext(self._clean_path())[1].lower()
        if ext in ENCODINGS and getattr(self, "_code", 200) == 200:   # not on error pages
            self.send_header("Content-Encoding", ENCODINGS[ext])
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cross-Origin-Resource-Policy", "same-origin")
        super().end_headers()

    def log_message(self, *args):
        pass


def main():
    if len(sys.argv) != 3:
        sys.exit("usage: serve.py <build folder> <port>")
    root, port = os.path.abspath(sys.argv[1]), int(sys.argv[2])
    if not os.path.isfile(os.path.join(root, "index.html")):
        sys.exit("no index.html in " + root)
    server = ThreadingHTTPServer(("127.0.0.1", port), partial(Handler, directory=root))
    server.daemon_threads = True
    server.serve_forever()


if __name__ == "__main__":
    main()
