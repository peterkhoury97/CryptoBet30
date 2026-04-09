// wwwroot/js/encryption.js
// AES-256 encryption for API calls (prevents DevTools inspection)

class ApiEncryption {
    constructor() {
        // These keys must match appsettings.json
        // In production: generate from server on login
        this.key = 'CHANGE_THIS_TO_32_BYTE_KEY_IN_PRODUCTION_NOW!'.substring(0, 32);
        this.iv = 'CHANGE_THIS_16B!'.substring(0, 16);
    }

    async encryptRequest(data) {
        const jsonString = JSON.stringify(data);
        const encrypted = await this.encrypt(jsonString);
        return encrypted;
    }

    async decryptResponse(encryptedData) {
        const decrypted = await this.decrypt(encryptedData);
        return JSON.parse(decrypted);
    }

    async encrypt(plainText) {
        const encoder = new TextEncoder();
        const data = encoder.encode(plainText);
        
        const keyBuffer = encoder.encode(this.key);
        const ivBuffer = encoder.encode(this.iv);
        
        const cryptoKey = await crypto.subtle.importKey(
            'raw',
            keyBuffer,
            { name: 'AES-CBC' },
            false,
            ['encrypt']
        );
        
        const encrypted = await crypto.subtle.encrypt(
            { name: 'AES-CBC', iv: ivBuffer },
            cryptoKey,
            data
        );
        
        return this.arrayBufferToBase64(encrypted);
    }

    async decrypt(base64String) {
        const encoder = new TextEncoder();
        const keyBuffer = encoder.encode(this.key);
        const ivBuffer = encoder.encode(this.iv);
        
        const cryptoKey = await crypto.subtle.importKey(
            'raw',
            keyBuffer,
            { name: 'AES-CBC' },
            false,
            ['decrypt']
        );
        
        const encryptedBuffer = this.base64ToArrayBuffer(base64String);
        
        const decrypted = await crypto.subtle.decrypt(
            { name: 'AES-CBC', iv: ivBuffer },
            cryptoKey,
            encryptedBuffer
        );
        
        const decoder = new TextDecoder();
        return decoder.decode(decrypted);
    }

    arrayBufferToBase64(buffer) {
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    }

    base64ToArrayBuffer(base64) {
        const binaryString = atob(base64);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }
        return bytes.buffer;
    }
}

// Encrypted fetch wrapper
async function encryptedFetch(url, options = {}) {
    const encryption = new ApiEncryption();
    
    // Encrypt request body if present
    if (options.body) {
        const encryptedBody = await encryption.encryptRequest(JSON.parse(options.body));
        options.body = encryptedBody;
        options.headers = {
            ...options.headers,
            'Content-Type': 'text/plain',
            'X-Encrypted': 'true'
        };
    }
    
    // Make request
    const response = await fetch(url, options);
    
    // Decrypt response if encrypted
    if (response.headers.get('X-Encrypted') === 'true') {
        const encryptedText = await response.text();
        const decryptedData = await encryption.decryptResponse(encryptedText);
        
        // Return fake Response object with decrypted data
        return {
            ok: response.ok,
            status: response.status,
            json: async () => decryptedData,
            text: async () => JSON.stringify(decryptedData)
        };
    }
    
    return response;
}

// Export for use in Blazor
window.encryptedFetch = encryptedFetch;
