const DB_VERSION_KEY='DBVersion';

function fallbackCopyTextToClipboard(text)
{
  var textArea=document.createElement("textarea");
  textArea.value=text;

  // Avoid scrolling to bottom
  textArea.style.top="0";
  textArea.style.left="0";
  textArea.style.position="fixed";

  document.body.appendChild(textArea);
  textArea.focus();
  textArea.select();

  try
  {
    var successful=document.execCommand('copy');
    var msg=successful? 'successful':'unsuccessful';
    console.log('Fallback: Copying text command was '+msg);
  } catch (err)
  {
    console.error('Fallback: Oops, unable to copy',err);
  }

  document.body.removeChild(textArea);
}

function copyTextToClipboard(text)
{
  if (!navigator.clipboard)
  {
    fallbackCopyTextToClipboard(text);
    return;
  }
  navigator.clipboard.writeText(text).then(function ()
  {
    const MAX_NOTIFICATION_TEXT_LEN=50;
    console.log('Async: Copying to clipboard was successful!');
    showNotificationInWallet(`Successfully copied ${text.length>MAX_NOTIFICATION_TEXT_LEN? `${text.substring(0,MAX_NOTIFICATION_TEXT_LEN)}...`:text} to clipboard.`)
  },function (err)
  {
    console.error('Async: Could not copy text: ',err);
    showNotificationInWallet('Could not copy text to clipboard');
  });

}

function showNotificationInWallet(text)
{
  const notificationDom=document.querySelector('.notification');
  if (notificationDom.innerText.length===0)
  {
    notificationDom.innerText=text;
    notificationDom.classList.add('open');
    setTimeout(() => notificationDom.classList.remove('open'),2500);
    setTimeout(() => notificationDom.innerText='',3000);
  }
}

var dataTypes=[ "WalletAddress","Transaction","Output","Swap","Order" ];
var dataStore={};

async function getData(walletName,dotNetObject)
{
  var dataObj={};

  for (var dataType in dataTypes)
  {
    const data=await idbKeyval.get(`${walletName}/${dataTypes[ dataType ]}`);
    if (data)
    {
      dataObj[ dataTypes[ dataType ] ]=data;
    }
  }

  var result=[];
  dataTypes.forEach(dataType =>
  {
    var typeData=dataObj[ dataType ]||{};
    result=[ ...result,...Object.entries(typeData).map(keyVal => ({
      type: dataType,
      id: keyVal[ 0 ],
      data: keyVal[ 1 ]
    })) ]
  })

  var dbVersion=await idbKeyval.get(`${walletName}/${DB_VERSION_KEY}`);
  if (typeof dbVersion==="undefined")
  {
    dbVersion=0;
  }

  dotNetObject.invokeMethodAsync('LoadWallet',JSON.stringify(result),dbVersion);
}

function saveDBVersion(walletName,dbVersion)
{
  idbKeyval.set(`${walletName}/${DB_VERSION_KEY}`,dbVersion);
}

function deleteData(table,walletName)
{
  const dbKey=`${walletName}/${table}`;
  idbKeyval.del(dbKey);
}

function saveData(type,walletName,dbId,value)
{
  const dbKey=`${walletName}/${type}`;
  data=dataStore[ dbKey ];

  if (!data)
  {
    dataStore[ dbKey ]={};
    dataStore[ dbKey ][ dbId ]=value;
    saveToStore(walletName);
    setDataUnsync();
  } else
  {
    if (data[ dbId ]!==value)
    {
      dataStore[ dbKey ][ dbId ]=value;
      saveToStore(walletName);
      setDataUnsync();
    }
  }
}

async function syncWithDb(walletName)
{
  for (var i=0;i<dataTypes.length;i++)
  {
    var dataType=dataTypes[ i ];

    var typeDataInMemory=dataStore[ `${walletName}/${dataType}` ];

    if (typeDataInMemory)
    {
      var objEntries=Object.entries(typeDataInMemory);

      for (var x=0;x<objEntries.length;x++)
      {
        var typeDataInStore=await idbKeyval.get(`${walletName}/${dataType}`);

        if (!typeDataInStore)
        {
          typeDataInStore={}
        }

        var keyVal=objEntries[ x ];
        if (Object.keys(typeDataInStore).includes(keyVal[ 0 ]))
        {
          // if value exist in store but data is different
          if (typeDataInStore[ keyVal[ 0 ] ]!==keyVal[ 1 ])
          {
            await idbKeyval.set(`${walletName}/${dataType}`,{
              ...typeDataInStore,
              ...{
                [ keyVal[ 0 ] ]: keyVal[ 1 ],
              },
            });
          }
        } else
        {
          // if value dont exist in store
          await idbKeyval.set(`${walletName}/${dataType}`,{
            ...{
              [ keyVal[ 0 ] ]: keyVal[ 1 ],
            },
            ...typeDataInStore,
          });
        }
      }
    }
  }
  setDataSync();
}

function debounce(func,wait,immediate)
{
  var timeout;
  return function executedFunction()
  {
    var context=this;
    var args=arguments;
    var later=function ()
    {
      timeout=null;
      if (!immediate) func.apply(context,args);
    };

    var callNow=immediate&&!timeout;
    clearTimeout(timeout);
    timeout=setTimeout(later,wait);
    if (callNow) func.apply(context,args);
  };
};

window.saveToStore=debounce(syncWithDb,1500);

var UIdataIndicator;
var SYNC_DATA_TEXT="Data saved ✔️";
var UNSYNC_DATA_TEXT="Saving data...";

function getUIdataSyncElement()
{
  UIdataIndicator=document.getElementById("js-datasaved-ui");
}

function setDataUnsync()
{
  if (UIdataIndicator)
  {
    if (UIdataIndicator.innerText===UNSYNC_DATA_TEXT)
    {
      return;
    }
    UIdataIndicator.innerText=UNSYNC_DATA_TEXT;
    UIdataIndicator.classList.add("text-danger");
  } else
  {
    var UIdataIndicator=document.getElementById("js-datasaved-ui");
    if (UIdataIndicator)
    {
      UIdataIndicator.innerText=UNSYNC_DATA_TEXT;
      UIdataIndicator.classList.add("text-danger");
    }
  }
}

function setDataSync()
{
  if (UIdataIndicator)
  {
    if (UIdataIndicator.innerText===SYNC_DATA_TEXT)
    {
      return;
    }
    UIdataIndicator.innerText=SYNC_DATA_TEXT;
    UIdataIndicator.classList.remove("text-danger");
  } else
  {
    var UIdataIndicator=document.getElementById("js-datasaved-ui");
    if (UIdataIndicator)
    {
      UIdataIndicator.innerText=SYNC_DATA_TEXT;
      UIdataIndicator.classList.remove("text-danger");
    }
  }
}
// trading scripts


function initOnReady()
{
  var widget=window.tvWidget=new TradingView.widget({
    debug: false, // uncomment this line to see Library errors and warnings in the console
    fullscreen: false,
    width: 600,
    height: 400,
    autosize: true,

    symbol: 'XTZBTC',
    interval: '30',
    container_id: "tv_chart_container",

    //  BEWARE: no trailing slash is expected in feed URL
    datafeed: new Datafeeds.UDFCompatibleDatafeed('http://3.127.178.86:5000/v1'),
    library_path: "charting_library/",
    locale: "en",
    disabled_features: [
      "left_toolbar",
      "header_compare",
      "header_indicators",
      "header_fullscreen_button",
      "header_saveload",
      "header_screenshot",
      "linetoolpropertieswidget_template_button",
      "compare_symbol",
      "edit_buttons_in_legend",
      "create_volume_indicator_by_default",
      "property_pages",
    ],
    // preset:'mobile',
    client_id: 'tradingview.com',
    user_id: 'public_user_id',
    theme: 'dark',
  });
};

function showNotification(title,data,icon)
{
  if (notificationsReady)
  {
    new Notification(title,{
      body: data,
      icon
    });
  }
}

var notificationsReady=false;


function dragTable()
{
  var p=document.querySelector('.tableFixHead');
  if (!p||p.classList.contains('no-js'))
  {
    return;
  }
  var section=p.classList[ 1 ];
  if (p.classList.length>1)
  {
    var savedHeight=localStorage.getItem(section);
    if (savedHeight)
    {
      p.style.height=savedHeight;
    }
  }

  var startX,startY,startWidth,startHeight;

  function doDrag(e)
  {
    p.style.height=(startHeight-e.clientY+startY)+'px';
    localStorage.setItem(section,p.style.height);
  }

  function stopDrag(e)
  {
    document.documentElement.removeEventListener('mousemove',doDrag,false);
    document.documentElement.removeEventListener('mouseup',stopDrag,false);
  }

  function initDrag(e)
  {
    startX=e.clientX;
    startY=e.clientY;
    startWidth=parseInt(document.defaultView.getComputedStyle(p).width,10);
    startHeight=parseInt(document.defaultView.getComputedStyle(p).height,10);
    document.documentElement.addEventListener('mousemove',doDrag,false);
    document.documentElement.addEventListener('mouseup',stopDrag,false);
  }

  var resizer=document.querySelector(".js-resize-height");
  resizer.addEventListener('mousedown',initDrag,false);
}

function selectBaker(bakerIndex)
{
  var bakerList=document.querySelector(".exchange-dropdown.baker");
  if (bakerList)
  {
    bakerList.scrollTo({
      top: bakerList.firstElementChild.offsetHeight*bakerIndex,
      behavior: "smooth"
    })
  }
}


function signOut(text)
{
  window.location.href="/";
}

function focusPass()
{
  setTimeout(() =>
  {
    try
    {
      document.querySelector('input[name="storagePassword"]').focus();
    } catch { }
  },100)
}

function walletLoaded()
{
  setTimeout(() => { notificationsReady=true; },15000);
}