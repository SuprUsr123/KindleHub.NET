using System;
using System.Linq;

namespace KindleHub.Client.Models;

/// <summary>
/// The App Store polyfill layer: everything the official web client injects around
/// a downloaded store app before running it, ported one-for-one from the live
/// kindlehub.pro runtime (kh-app.js — _khWrapAppSecure, _khFrameWrite,
/// _khAppGuardShim, _khAppStorageShim, _khAppScreenshotShim, _khAppDialogShim),
/// plus a local bridge that reproduces the parent-side postMessage contract
/// (saveLocal / preview / cloudGet / cloudPut / cloudList / sendMail) so a store
/// app launched from the desktop behaves exactly like it does on the website.
///
/// The wrapped file keeps the web sandbox's guarantees: network is refused by the
/// same CSP, forms and WebRTC are disabled, storage is scoped per app id under the
/// same kh_appls_ key the website uses, timers are throttled the same way (12 FPS
/// on a Kindle, 30 elsewhere), and the app's postMessage bridge calls get the
/// same replies and error strings as the live site.
/// </summary>
public static class AppPolyfills
{
    /// <summary>The exact CSP the web runtime seals every app with (_khAppCsp).</summary>
    public const string AppCsp =
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; " +
        "script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; " +
        "media-src data: blob:; font-src data:; connect-src 'none'; form-action 'none'; " +
        "base-uri 'none'; frame-src 'none'\">";

    /// <summary>localStorage cap the web parent enforces on saveLocal (KH_APP_LS_MAX).</summary>
    public const int LocalStoreMax = 65536;

    // Ported from _khFrameWrite's sealed-frame injection: an e-ink aware
    // requestAnimationFrame/setTimeout/setInterval throttle. On a Kindle browser
    // (INK=1) the page runs 12 FPS with a 33 ms floor and devicePixelRatio locked
    // to 1; everywhere else 30 FPS with a 16 ms floor. Hidden tabs skip intervals.
    private const string PerfShim = """
        <script>(function(){try{var W=window,INK=(/Kindle|Silk/i.test(navigator.userAgent||""))?1:0,FPS=INK?12:30,MS=1000/FPS,MIN=INK?33:16;var q=[],ids=0,run=false,last=0,rt=null;function tick(){rt=null;if(document.hidden){rt=setT(tick,250);return;}var now=(W.performance&&performance.now)?performance.now():Date.now();var wait=MS-(now-last);if(wait>2){rt=setT(tick,wait);return;}last=now;var cur=q;q=[];for(var i=0;i<cur.length;i++){if(cur[i]){try{cur[i].f(now);}catch(e){setT(function(){throw e;},0);}}}if(q.length)rt=setT(tick,MS);}var setT=W.setTimeout.bind(W),setI=W.setInterval.bind(W);W.requestAnimationFrame=function(f){var id=++ids;q.push({id:id,f:f});if(!rt)rt=setT(tick,Math.max(0,MS-(Date.now()-last)));return id;};W.cancelAnimationFrame=function(id){for(var i=0;i<q.length;i++)if(q[i]&&q[i].id===id)q[i]=null;};W.webkitRequestAnimationFrame=W.requestAnimationFrame;W.webkitCancelAnimationFrame=W.cancelAnimationFrame;W.setInterval=function(f,d){var a=[].slice.call(arguments,2);return setI(function(){if(document.hidden)return;f.apply(W,a);},Math.max(MIN,+d||0));};W.setTimeout=function(f,d){var a=[].slice.call(arguments,2);return setT(function(){f.apply(W,a);},(+d||0)<4?(+d||0):Math.max(+d,0));};if(INK){try{Object.defineProperty(W,"devicePixelRatio",{get:function(){return 1;}});}catch(e){}}}catch(e){}})();</script>
        """;

    // Ported from _khAppGuardShim: announces departure (bye), re-arms on
    // document.open/write/writeln, blocks every form submission, and removes
    // WebRTC so an app can never punch a hole around the CSP.
    private const string GuardShim = """
        <script>(function(){try{var W=window,P=W.parent;var bye=function(){try{P.postMessage({kh:"app",action:"bye",nonce:W.KH_APP_NONCE},"*");}catch(e){}};var arm=function(){try{W.addEventListener("beforeunload",bye,true);W.addEventListener("pagehide",bye,true);}catch(e){}};arm();var D=W.Document&&W.Document.prototype;var wrap=function(o,k){try{var f=o[k];if(typeof f!=="function")return;Object.defineProperty(o,k,{value:function(){var r=f.apply(this,arguments);arm();return r;},writable:false,configurable:false});}catch(e){}};if(D){wrap(D,"open");wrap(D,"write");wrap(D,"writeln");}try{W.addEventListener("submit",function(e){try{e.preventDefault();}catch(x){}},true);}catch(e){}try{var F=W.HTMLFormElement&&W.HTMLFormElement.prototype;if(F)Object.defineProperty(F,"submit",{value:function(){},writable:false,configurable:false});}catch(e){}var R=["RTCPeerConnection","webkitRTCPeerConnection","mozRTCPeerConnection","RTCDataChannel","RTCSessionDescription","RTCIceCandidate"];for(var i=0;i<R.length;i++){var k=R[i];try{delete W[k];}catch(e){}try{Object.defineProperty(W,k,{value:undefined,writable:false,configurable:false});}catch(e){try{W[k]=undefined;}catch(e2){}}}}catch(_e){}})();</script>
        """;

    // Ported from _khAppStorageShim + the parent's saveLocal handler. The web
    // seeds the iframe with a snapshot of kh_appls_<scope> and persists debounced
    // writes back through the bridge; here the same snapshot is read directly
    // (same key, same cap, same error strings) and saved through the same
    // postMessage contract, which the local bridge below answers.
    private const string StorageShim = """
        <script>(function(){try{var m={},raw=null;try{raw=window.__khNativeLocalStorage.getItem("kh_appls___KH_SCOPE__");}catch(e){raw=null;}try{m=(raw&&JSON.parse(raw))||{};if(!m||typeof m!=="object"||Array.isArray(m))m={};}catch(e){m={};}var N=window.KH_APP_NONCE;var _t=0,_last=0;function _flush(){_t=0;_last=(new Date()).getTime();try{parent.postMessage({kh:"app",action:"saveLocal",nonce:N,data:JSON.stringify(m)},"*");}catch(e){}}function _save(){try{var now=(new Date()).getTime();if(!_t&&now-_last>500){_flush();return;}if(_t)clearTimeout(_t);_t=setTimeout(_flush,400);}catch(e){}}var api={getItem:function(k){k=String(k);return Object.prototype.hasOwnProperty.call(m,k)?m[k]:null;},setItem:function(k,v){m[String(k)]=String(v);_save();},removeItem:function(k){delete m[String(k)];_save();},clear:function(){m={};_save();},key:function(i){var a=Object.keys(m);return i<a.length?a[i]:null;}};Object.defineProperty(api,"length",{get:function(){return Object.keys(m).length;}});Object.defineProperty(window,"localStorage",{value:api,configurable:true});Object.defineProperty(window,"sessionStorage",{value:api,configurable:true});}catch(_e){}})();</script>
        """;

    // Ported from _khAppScreenshotShim: after the page settles, the largest
    // canvas is downscaled to at most 280 px and posted as a JPEG preview
    // (the website uses it as the store listing thumbnail).
    private const string ScreenshotShim = """
        <script>(function(){try{var N=window.KH_APP_NONCE;setTimeout(function(){try{var cs=document.getElementsByTagName("canvas"),best=null,bestArea=0,i,a;for(i=0;i<cs.length;i++){a=(cs[i].width||0)*(cs[i].height||0);if(a>bestArea){bestArea=a;best=cs[i];}}if(!best||bestArea<400)return;var MAXD=280,w=best.width,h=best.height,scale=Math.min(1,MAXD/Math.max(w,h));var ow=Math.max(1,Math.round(w*scale)),oh=Math.max(1,Math.round(h*scale));var oc=document.createElement("canvas");oc.width=ow;oc.height=oh;var octx=oc.getContext("2d");if(!octx)return;octx.drawImage(best,0,0,ow,oh);var q=0.75,url="",t;for(t=0;t<4;t++){url=oc.toDataURL("image/jpeg",q);if(url.length<=16000)break;q-=0.18;}if(url.length>18000)return;parent.postMessage({kh:"app",action:"preview",nonce:N,img:url},"*");}catch(e){}},3500);}catch(e){}})();</script>
        """;

    // Ported from _khAppDialogShim: alert/confirm/prompt as in-page boxes,
    // matching what the sandboxed iframe shows (confirm answers true, prompt
    // returns the default).
    private const string DialogShim = """
        <script>(function(){try{var K="__khdlg";function box(msg,extra){try{var d=document.getElementById(K);if(d)d.parentNode.removeChild(d);d=document.createElement("div");d.id=K;d.setAttribute("style","position:fixed;left:0;top:0;right:0;bottom:0;z-index:2147483647;background:rgba(0,0,0,.45);display:block;font-family:inherit;");var c=document.createElement("div");c.setAttribute("style","position:absolute;left:50%;top:50%;margin:0;transform:translate(-50%,-50%);max-width:82%;min-width:180px;background:#fff;color:#111;border:2px solid #111;border-radius:10px;padding:16px 18px;text-align:center;");var t=document.createElement("div");t.setAttribute("style","font-size:15px;line-height:1.45;margin-bottom:14px;word-wrap:break-word;");t.appendChild(document.createTextNode(String(msg)));c.appendChild(t);if(extra)c.appendChild(extra);var b=document.createElement("button");b.appendChild(document.createTextNode("OK"));b.setAttribute("style","font:inherit;font-size:15px;font-weight:700;padding:9px 26px;min-height:42px;border:2px solid #111;border-radius:8px;background:#111;color:#fff;cursor:pointer;");b.onclick=function(){try{d.parentNode.removeChild(d);}catch(e){}};c.appendChild(b);d.appendChild(c);(document.body||document.documentElement).appendChild(d);try{b.focus();}catch(e){}}catch(e){}}window.alert=function(m){box(m===undefined?"":m);};window.confirm=function(m){box(m===undefined?"":m);return true;};window.prompt=function(m,dflt){box(m===undefined?"":m);return (dflt===undefined?"":dflt);};}catch(_e){}})();</script>
        """;

    // A fresh nonce per launch, exactly like _khFrameNonce in the web runtime.
    private const string NonceShim = """
        <script>(function(){try{var e="";try{var t=new Uint32Array(4);window.crypto.getRandomValues(t);for(var n=0;n<t.length;n++)e+=("00000000"+t[n].toString(16)).slice(-8);}catch(t){e="";}if(e.length<32){e="";for(var r=0;r<4;r++)e+=("00000000"+Math.floor(4294967296*Math.random()).toString(16)).slice(-8);}window.KH_APP_NONCE=e;}catch(e){}})();</script>
        """;

    // The desktop's stand-in for the website's parent-side message handler
    // (kh-app.js @807200): it answers the same {kh:"app",...} protocol with the
    // same reply shapes and error strings.
    //
    // Topology fix: on the website the app lives in an iframe, so
    // parent.postMessage() delivers requests to the parent WITHOUT echoing them
    // back to the app; only the parent's replies arrive as message events. In a
    // standalone page parent === window, so a raw postMessage would let the app
    // overhear its own request (its rid-matching listener would fire on the
    // request, where ok is undefined, before the real reply lands). The bridge
    // therefore re-wraps window.postMessage: kh:app requests are handed straight
    // to the handler — exactly the parent's view — while replies go out through
    // the native postMessage so they arrive as real message events, like the
    // website's t.source.postMessage replies.
    //
    // Cloud saves emulate the kh_app_get/kh_app_put/kh_app_list RPCs against
    // localStorage on this device (the website's copy lives server-side; a
    // standalone file has no credentials and its CSP refuses network, so this
    // is the closest exact-behaviour match).
    private const string BridgeShim = """
        <script>(function(){try{var W=window;var scope="__KH_SCOPE__";var LS="kh_appls_"+scope;var native=null;try{native=W.__khNativeLocalStorage;}catch(e){native=null;}if(!native){try{native=W.localStorage;}catch(e){native=null;}}function lsGet(k){try{return native?native.getItem(k):null;}catch(e){return null;}}function lsSet(k,v){try{if(native)native.setItem(k,v);}catch(e){}}function lsDel(k){try{if(native)native.removeItem(k);}catch(e){}}function kvKey(room){return "kh_appkv_"+scope+"_"+room;}function kvRead(room){try{var o=JSON.parse(lsGet(kvKey(room))||"{}");return (o&&typeof o==="object"&&!Array.isArray(o))?o:{};}catch(e){return {};}}function kvWrite(room,o){try{lsSet(kvKey(room),JSON.stringify(o));}catch(e){}}var lastMail=0;var nativePost=W.postMessage.bind(W);function handle(ev){try{var d=ev&&ev.data;if(!d||typeof d!=="object"||d.kh!=="app")return;if(ev.source!==W)return;if(typeof d.ok!=="undefined")return;var nonce=W.KH_APP_NONCE;if(nonce&&String(d.nonce||"")!==String(nonce))return;function ack(ok,err){try{var out={kh:"app",action:d.action,rid:d.rid,ok:!!ok};if(err)out.error=String(err);nativePost(out,"*");}catch(e){}}function data(payload){try{nativePost(Object.assign({kh:"app",action:d.action,rid:d.rid,ok:true},payload||{}),"*");}catch(e){}}if(d.action==="saveLocal"){var s=String(null==d.data?"":d.data);if(s.length>__KH_LS_MAX__)return ack(false,"this app has saved too much (max "+Math.round(__KH_LS_MAX__/1024)+" KB)");var obj;try{obj=JSON.parse(s);}catch(e){return ack(false,"bad save");}if(!obj||typeof obj!=="object"||Array.isArray(obj))return ack(false,"bad save");try{if("{}"===s)lsDel(LS);else lsSet(LS,s);ack(true);}catch(e){ack(false,"this device is out of storage, so the save did not stick");}return;}if(d.action==="preview"){var img=String(d.img||"");if(!img||img.length>20000||0!==img.indexOf("data:image/"))return;try{lsSet("kh_appprev_"+scope,img);}catch(e){}return;}if(d.action==="cloudGet"||d.action==="cloudPut"||d.action==="cloudList"){var room=String(d.room||"main").replace(/[^A-Za-z0-9_.-]/g,"").slice(0,64)||"main";if(d.action==="cloudPut"){var key=String(d.key||"").slice(0,128);if(!key)return ack(false,"no key");var map=kvRead(room);map[key]={v:String(null==d.value?"":d.value).slice(0,__KH_LS_MAX__),by:"me",at:Date.now()};kvWrite(room,map);ack(true);return;}if(d.action==="cloudGet"){var k2=String(d.key||"").slice(0,128);var got=kvRead(room)[k2];data({v:got?got.v:null});return;}var rows=[],m2=kvRead(room);for(var r in m2)if(Object.prototype.hasOwnProperty.call(m2,r))rows.push({k:r,v:m2[r].v,by:m2[r].by});data({rows:rows});return;}if(d.action==="sendMail"){if(Date.now()<lastMail)return ack(false,"too many requests, try again in a moment");lastMail=Date.now()+1500;var to=String(d.to||"").trim().slice(0,120);if(!to)return ack(false,"no recipient");var box2=document.createElement("div");box2.setAttribute("style","position:fixed;left:0;top:0;right:0;bottom:0;z-index:2147483646;background:rgba(0,0,0,.45);");var card=document.createElement("div");card.setAttribute("style","position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);max-width:82%;min-width:200px;background:#fff;color:#111;border:2px solid #111;border-radius:10px;padding:16px 18px;text-align:center;");var msg=document.createElement("div");msg.setAttribute("style","font-size:14px;line-height:1.5;margin-bottom:12px;");msg.appendChild(document.createTextNode('An app wants to send an email to '+to+' from your KindleHub account. It is an app, not KindleHub.'));var btns=document.createElement("div");function close(){try{box2.parentNode.removeChild(box2);}catch(e){}}function send(){close();ack(false,"mail is unavailable");}function cancel(){close();ack(false,"declined by user");}var sb=document.createElement("button");sb.textContent="Send";sb.setAttribute("style","font:inherit;font-weight:700;padding:9px 22px;margin:0 6px;min-height:42px;border:2px solid #111;border-radius:8px;background:#111;color:#fff;");sb.onclick=send;var cb=document.createElement("button");cb.textContent="Cancel";cb.setAttribute("style","font:inherit;padding:9px 22px;margin:0 6px;min-height:42px;border:2px solid #111;border-radius:8px;background:#fff;color:#111;");cb.onclick=cancel;btns.appendChild(sb);btns.appendChild(cb);card.appendChild(msg);card.appendChild(btns);box2.appendChild(card);(document.body||document.documentElement).appendChild(box2);return;}}catch(e){}}W.postMessage=function(msg,tOrigin,transfer){try{if(msg&&typeof msg==="object"&&msg.kh==="app"&&!("ok" in msg)){handle({source:W,data:msg});return;}}catch(e){}return nativePost(msg,tOrigin,transfer);};W.addEventListener("message",function(ev){try{var d=ev&&ev.data;if(!d||typeof d!=="object"||d.kh!=="app")return;if(typeof d.ok!=="undefined")return;handle(ev);}catch(e){}},false);}catch(e){}})();</script>
        """;

    // Legacy helpers kept from the desktop's first polyfill layer so apps built
    // against it (NOW(), saveGame(), _khRunSave/_khRunLoad) keep working.
    private const string LegacyShim = """
        <script>(function(){window._khRunSave=window._khRunSave||function(k,v){try{window.localStorage.setItem("run:"+k,JSON.stringify(v==null?null:v));}catch(e){}};window._khRunLoad=window._khRunLoad||function(k){try{var v=window.localStorage.getItem("run:"+k);return v==null?null:JSON.parse(v);}catch(e){return null;}};window.NOW=window.NOW||function(){return new Date();};window.saveGame=window.saveGame||function(){};})();</script>
        """;

    // The storage shim replaces window.localStorage before the bridge can grab a
    // native reference, so the bridge is handed the real one first.
    private const string NativeStoreShim = """
        <script>(function(){try{var p="__kh_probe__";window.localStorage.setItem(p,"1");window.localStorage.removeItem(p);window.__khNativeLocalStorage=window.localStorage;}catch(e){try{Object.defineProperty(window,"__khNativeLocalStorage",{value:null,configurable:true});}catch(e2){}}})();</script>
        """;

    /// <summary>
    /// Wraps a store app's HTML with the full web-build runtime, in the exact order
    /// the live site injects it: CSP → perf throttle → guard → scoped storage →
    /// screenshot → dialogs → nonce, then the app itself.
    /// </summary>
    public static string WrapForStandalone(string html, string appId)
    {
        var scope = SanitizeScope(appId);
        var shims = AppCsp
                  + NativeStoreShim
                  + PerfShim
                  + NonceShim
                  + GuardShim
                  + StorageShim
                  + ScreenshotShim
                  + DialogShim
                  + BridgeShim
                  + LegacyShim;
        shims = shims
            .Replace("__KH_SCOPE__", scope, StringComparison.Ordinal)
            .Replace("__KH_LS_MAX__", LocalStoreMax.ToString(), StringComparison.Ordinal);

        html ??= "";
        var head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (head >= 0)
        {
            var end = html.IndexOf('>', head);
            if (end >= 0) return html.Insert(end + 1, shims);
        }

        var htmlTag = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlTag >= 0)
        {
            var end = html.IndexOf('>', htmlTag);
            if (end >= 0) return html.Insert(end + 1, "<head>" + shims + "</head>");
        }

        return "<!doctype html><html><head><meta charset=\"utf-8\">" + shims + "</head><body>" + html + "</body></html>";
    }

    /// <summary>
    /// The scope an app id maps to. Mirrors _khAppCloudId/_khAppLsKey: keep the
    /// safe characters, cap the length, and prefix anything that had to change.
    /// </summary>
    public static string SanitizeScope(string appId)
    {
        var raw = appId ?? "";
        var safe = new string(raw.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length > 48) safe = safe[..48];
        return safe.Length == 0 ? "app" : safe;
    }
}
