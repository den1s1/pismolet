using System.Text;

namespace Pismolet.Web.Endpoints;

public static class RecipientManagementAjaxMiddlewareExtensions
{
    public static IApplicationBuilder UseRecipientManagementAjax(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!ShouldTransform(context.Request.Path))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next();

        buffer.Position = 0;
        if (context.Response.StatusCode != StatusCodes.Status200OK || context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) != true)
        {
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
            return;
        }

        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var html = await reader.ReadToEndAsync();
        var transformed = ShouldInject(html) ? Inject(html) : html;
        var bytes = Encoding.UTF8.GetBytes(transformed);

        context.Response.Body = originalBody;
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes);
    });

    private static bool ShouldTransform(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.StartsWith("/mailings/", StringComparison.OrdinalIgnoreCase)
            && value.EndsWith("/recipients", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldInject(string html) =>
        html.Contains("/recipients/add", StringComparison.Ordinal)
        && html.Contains("Управление адресами", StringComparison.Ordinal)
        && !html.Contains("recipient-management-ajax-script", StringComparison.Ordinal);

    private static string Inject(string html) => html.Replace("</body>", Script() + "\n</body>", StringComparison.OrdinalIgnoreCase);

    private static string Script() => """
<script class="recipient-management-ajax-script">
(function(){
  function manager(){var f=document.querySelector("form[action$='/recipients/add']");return f&&(f.closest('section.address-list-block')||f.closest('section.inline-base-confirm'));}
  function stats(){return document.querySelector('.stats.import-summary');}
  function markBusy(on){var m=manager();if(m){m.style.opacity=on?'0.55':'';m.style.pointerEvents=on?'none':'';}}
  function replaceFrom(html){
    var doc=new DOMParser().parseFromString(html,'text/html');
    var nextAdd=doc.querySelector("form[action$='/recipients/add']");
    var nextManager=nextAdd&&(nextAdd.closest('section.address-list-block')||nextAdd.closest('section.inline-base-confirm'));
    var currentManager=manager();
    if(nextManager&&currentManager){currentManager.replaceWith(nextManager);}
    var nextStats=doc.querySelector('.stats.import-summary');
    var currentStats=stats();
    if(nextStats&&currentStats){currentStats.replaceWith(nextStats);}
  }
  async function load(url,options){
    markBusy(true);
    try{
      var response=await fetch(url,Object.assign({headers:{'X-Requested-With':'fetch'}},options||{}));
      var html=await response.text();
      replaceFrom(html);
    }finally{markBusy(false);}
  }
  document.addEventListener('submit',function(e){
    var form=e.target;
    if(!(form instanceof HTMLFormElement)) return;
    if(!manager()||!manager().contains(form)) return;
    if(form.method.toLowerCase()==='get'){
      e.preventDefault();
      var url=form.action+'?'+new URLSearchParams(new FormData(form)).toString();
      history.replaceState(null,'',url);
      load(url);
      return;
    }
    if(form.action.indexOf('/recipients/add')!==-1||form.action.indexOf('/recipients/remove')!==-1){
      e.preventDefault();
      load(form.action,{method:'POST',body:new FormData(form)});
      if(form.action.indexOf('/recipients/add')!==-1){form.reset();}
    }
  });
  document.addEventListener('click',function(e){
    var link=e.target.closest&&e.target.closest('a');
    if(!link||!manager()||!manager().contains(link)) return;
    if(link.href.indexOf('/recipients')!==-1&&link.textContent.trim()==='Сбросить'){
      e.preventDefault();
      history.replaceState(null,'',link.href);
      load(link.href);
    }
  });
})();
</script>
""";
}
