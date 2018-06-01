(function() {

var requestProps = Object.keys(host.request);
for (var i = 0; i < requestProps.length; i++) {
    var requestKey = requestProps[i];
    var requestValue = host.request[requestKey];

    if (requestKey == 'queryString' || requestKey == 'headers') {
        host.echo(requestKey + ' => {');
        for (var j = 0; j < requestValue.length; j++) {
            host.echo('  ' + requestValue[j].name + ' = ' + requestValue[j].value);
        }
        host.echo('}');
    } else {
        host.echo(requestKey + ' = ' + requestValue);
    }
}

return {
    statusCode: 200,
    mimeType: "text/html",
    headers: {
        'foo': 'bar'
    },
    body: 'hello world'
};

})();
