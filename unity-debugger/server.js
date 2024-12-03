const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const net = require('net');
const path = require('path');

const app = express();
app.use(express.static(path.join(__dirname, 'public')));
app.use(express.json());

const server = http.createServer(app);
const wss = new WebSocket.Server({ server });

// 存储连接的设备信息和连接
const devices = new Map(); // deviceId -> { info: deviceInfo, socket: tcpSocket }
const webSocketClients = new Map(); // clientId -> { ws, currentDevice }

// TCP服务器设置
const tcpServer = net.createServer((socket) => {
  let deviceId = null;
  let buffer = '';
  let messageDepth = 0;
  let isInMessage = false;

  console.log('New TCP client connected');

  socket.on('data', (chunk) => {
    buffer += chunk.toString();
    
    try {
      let startIndex = 0;
      
      for (let i = 0; i < buffer.length; i++) {
        if (buffer[i] === '{') {
          if (!isInMessage) {
            isInMessage = true;
            startIndex = i;
          }
          messageDepth++;
        }
        else if (buffer[i] === '}') {
          messageDepth--;
          
          if (messageDepth === 0 && isInMessage) {
            // 找到完整的JSON消息
            const messageStr = buffer.substring(startIndex, i + 1);
            try {
              const message = JSON.parse(messageStr);
              handleMessage(message, socket);
            } catch (parseError) {
              console.error('Failed to parse message:', messageStr);
            }
            
            // 移除已处理的消息
            buffer = buffer.substring(i + 1);
            i = -1; // 重置索引
            isInMessage = false;
          }
        }
      }
      
      // 如果缓冲区太大但没有有效消息，清空它
      if (buffer.length > 1000000) { // 1MB
        console.warn('Buffer overflow, clearing');
        buffer = '';
        messageDepth = 0;
        isInMessage = false;
      }
      
    } catch (e) {
      console.error('Error processing data:', e);
      buffer = '';
      messageDepth = 0;
      isInMessage = false;
    }
  });

  socket.on('close', () => {
    if (deviceId) {
      console.log('Device disconnected:', deviceId);
      devices.delete(deviceId);
      notifyDeviceListChanged();
    }
  });

  socket.on('error', (err) => {
    console.error('Socket error:', err);
    if (deviceId) {
      devices.delete(deviceId);
      notifyDeviceListChanged();
    }
  });

  // 处理消息的函数
  function handleMessage(message, socket) {
    if (!message || typeof message !== 'object') return;

    switch (message.type) {
      case 'device_info':
        deviceId = message.data.id || `device-${Date.now()}`;
        devices.set(deviceId, {
          info: message.data,
          socket: socket
        });
        socket.deviceId = deviceId;
        notifyDeviceListChanged();
        console.log('Device registered:', deviceId);
        break;

      case 'log':
        if (!message.data) return;
        
        // 格式化日志数据
        const logData = {
          type: String(message.data.type || 'Log'),
          message: String(message.data.message || ''),
          stackTrace: String(message.data.stackTrace || ''),
          timestamp: String(message.data.timestamp || new Date().toISOString())
        };

        // 转发日志到WebSocket客户端
        for (const [clientId, client] of webSocketClients) {
          if (client.currentDevice === socket.deviceId) {
            try {
              client.ws.send(JSON.stringify({
                type: 'log',
                deviceId: socket.deviceId,
                data: logData
              }));
            } catch (e) {
              console.error('Error sending log to client:', e);
            }
          }
        }
        break;

      default:
        console.log('Unknown message type:', message.type);
    }
  }
});

// REST API endpoints
app.get('/api/devices', (req, res) => {
  const deviceList = Array.from(devices.entries()).map(([id, device]) => ({
    id,
    ...device.info
  }));
  res.json(deviceList);
});

// WebSocket连接处理
wss.on('connection', (ws) => {
  const clientId = Date.now().toString();
  console.log('New WebSocket client connected:', clientId);
  webSocketClients.set(clientId, { ws, currentDevice: null });

  ws.on('message', (message) => {
    try {
      const data = JSON.parse(message);
      handleWebSocketMessage(clientId, data);
    } catch (e) {
      console.error('Error processing WebSocket message:', e);
    }
  });

  ws.on('close', () => {
    console.log('WebSocket client disconnected:', clientId);
    webSocketClients.delete(clientId);
  });
});

function handleWebSocketMessage(clientId, data) {
  const client = webSocketClients.get(clientId);
  if (!client) return;

  switch (data.type) {
    case 'select_device':
      client.currentDevice = data.deviceId;
      console.log(`Client ${clientId} selected device ${data.deviceId}`);
      break;
      
    case 'execute_code':
      const device = devices.get(client.currentDevice);
      if (device && device.socket) {
        const message = JSON.stringify({
          type: 'execute_code',
          data: { code: data.code }
        }) + '\n';
        device.socket.write(message);
      }
      break;
  }
}

function notifyDeviceListChanged() {
  const deviceList = Array.from(devices.entries()).map(([id, device]) => ({
    id,
    ...device.info
  }));
  
  for (const [clientId, client] of webSocketClients) {
    try {
      client.ws.send(JSON.stringify({
        type: 'device_list_updated',
        devices: deviceList
      }));
    } catch (e) {
      console.error('Error notifying client:', e);
    }
  }
}

tcpServer.listen(8002, () => {
  console.log('TCP server started on port 8002');
});

const PORT = 3001;
server.listen(PORT, () => {
  console.log(`Server is running on http://localhost:${PORT}`);
});