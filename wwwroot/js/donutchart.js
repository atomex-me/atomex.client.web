function initChartService()
{
  Chart.pluginService.register({
    beforeDraw: function (chart)
    {
      if (chart.config.options.elements.center)
      {
        // Get ctx from string
        var ctx=chart.chart.ctx;

        // Get options from the center object in options
        var centerConfig=chart.config.options.elements.center;
        var fontStyle=centerConfig.fontStyle||'Arial';
        var txt=centerConfig.text;
        var color=centerConfig.color||'#000';
        var maxFontSize=centerConfig.maxFontSize||30;
        var sidePadding=centerConfig.sidePadding||20;
        var sidePaddingCalculated=(sidePadding/100)*(chart.innerRadius*2)
        // Start with a base font of 30px
        ctx.font="30px "+fontStyle;

        // Get the width of the string and also the width of the element minus 10 to give it 5px side padding
        var stringWidth=ctx.measureText(txt).width;
        var elementWidth=(chart.innerRadius*2)-sidePaddingCalculated;

        // Find out how much the font can grow in width.
        var widthRatio=elementWidth/stringWidth;
        var newFontSize=Math.floor(30*widthRatio);
        var elementHeight=(chart.innerRadius*2);

        // Pick a new font size so it will not be larger than the height of label.
        var fontSizeToUse=Math.min(newFontSize,elementHeight,maxFontSize);
        var minFontSize=centerConfig.minFontSize;
        var lineHeight=centerConfig.lineHeight||25;
        var wrapText=false;

        if (minFontSize===undefined)
        {
          minFontSize=20;
        }

        if (minFontSize&&fontSizeToUse<minFontSize)
        {
          fontSizeToUse=minFontSize;
          wrapText=true;
        }

        // Set font settings to draw it correctly.
        ctx.textAlign='center';
        ctx.textBaseline='middle';
        var centerX=((chart.chartArea.left+chart.chartArea.right)/2);
        var centerY=((chart.chartArea.top+chart.chartArea.bottom)/2);
        ctx.font=fontSizeToUse+"px "+fontStyle;
        ctx.fillStyle=color;

        if (!wrapText)
        {
          ctx.fillText(txt,centerX,centerY);
          return;
        }

        var words=txt.split(' ');
        var line='';
        var lines=[];

        // Break words up into multiple lines if necessary
        for (var n=0;n<words.length;n++)
        {
          var testLine=line+words[ n ]+' ';
          var metrics=ctx.measureText(testLine);
          var testWidth=metrics.width;
          if (testWidth>elementWidth&&n>0)
          {
            lines.push(line);
            line=words[ n ]+' ';
          } else
          {
            line=testLine;
          }
        }

        // Move the center up depending on line height and number of lines
        centerY-=(lines.length/2)*lineHeight;

        for (var n=0;n<lines.length;n++)
        {
          ctx.fillText(lines[ n ],centerX,centerY);
          centerY+=lineHeight;
        }
        //Draw text in center
        ctx.fillText(line,centerX,centerY);
      }
    }
  });
}

function drawChart(data,labels,totalDollars)
{
  var canv=document.getElementById('currencies-donut');
  if (!canv)
  {
    return;
  }
  var ctx=canv.getContext('2d');

  var animations=false;

  if (window.donutChart)
  {
    Chart.pluginService.clear();
    window.donutChart.destroy();
    delete window.donutChart;
    animations=true;
  }

  initChartService();

  var options={
    legend: {
      display: false
    },
    elements: {
      center: {
        text: `${totalDollars}$`,
        color: '#FFF',
        fontStyle: 'RobotoRegular',
        sidePadding: 20,
        minFontSize: 28,
        maxFontSize: 30,
        lineHeight: 25
      }
    },

    tooltips: {
      callbacks: {
        title: function (tooltipItem,data)
        {
          return data[ 'labels' ][ tooltipItem[ 0 ][ 'index' ] ];
        },
        label: function (tooltipItem,data)
        {
          var dataset=data[ 'datasets' ][ 0 ];
          var percent=(dataset[ 'data' ][ tooltipItem[ 'index' ] ]/dataset[ "_meta" ][ Object.keys(dataset[ "_meta" ])[ 0 ] ][ 'total' ])*100;
          return `$${data[ 'datasets' ][ 0 ][ 'data' ][ tooltipItem[ 'index' ] ]} (${percent.toFixed(1)}%)`;
        }
      },
      backgroundColor: '#0E1422',
      titleFontSize: 16,
      titleFontColor: '#80817D',
      bodyFontColor: '#FFF',
      bodyFontSize: 14,
      fontStyle: 'RobotoLight',
      displayColors: false
    }
  }

  if (!animations)
  {
    options[ 'animation' ]=false;
  }

  window.donutChart=new Chart(ctx,{
    type: 'doughnut',
    data: {
      labels: labels,
      datasets: [ {
        data: data,
        borderWidth: 0,
        backgroundColor: [
          '#003f5c',
          '#444e86',
          '#955196',
          '#dd5182',
          '#ff6e54',
          '#ffa600'
        ]
      } ]
    },
    options
  });
}

function updateChart(data,labels,totalDollars)
{
  if (window.donutChart&&document.getElementById('currencies-donut'))
  {
    window.donutChart.options.elements.center.text=`${totalDollars}$`;
    window.donutChart.data.datasets[ 0 ].data=data;
    window.donutChart.data.labels=labels;
    window.donutChart.update();
  }
}