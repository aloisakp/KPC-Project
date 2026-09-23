'use strict';
// No credentials or account messages: verify the inner pinned tester health route.
const tls=require('node:tls'),crypto=require('node:crypto');
const pin='3630195b7fd5c1e7a60080b367d82da922288b38e2975c4c81e774a03460e389';
const socket=tls.connect({host:'127.0.0.1',port:11107,rejectUnauthorized:false,minVersion:'TLSv1.2'});
let response='',verified=false;
const timer=setTimeout(()=>socket.destroy(Error('Relay connection timed out')),7000);
socket.once('secureConnect',()=>{
 const cert=socket.getPeerCertificate();
 if(!cert.raw||crypto.createHash('sha256').update(cert.raw).digest('hex')!==pin||
    Date.parse(cert.valid_from)>Date.now()||Date.parse(cert.valid_to)<=Date.now()){
  socket.destroy(Error('Relay certificate verification failed'));return;
 }
 verified=true;socket.write('GET /tester/v1/health HTTP/1.1\r\nHost: 127.0.0.1:11107\r\nConnection: close\r\n\r\n');
});
socket.on('data',data=>{response+=data;if(response.length>4096)socket.destroy(Error('Invalid health response'));});
socket.on('error',()=>{process.exitCode=1;});
socket.once('close',()=>{clearTimeout(timer);if(!verified||!response.startsWith('HTTP/1.1 200 '))process.exitCode=1;});
