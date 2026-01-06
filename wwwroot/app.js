// BunBun B-Roll Generator - Client-side utilities

/**
 * Trigger a file download directly from a URL
 * Uses fetch + blob to enable custom filename for cross-origin downloads
 */
async function triggerDownload(url, filename) {
    try {
        // Fetch the file as blob to enable custom filename
        const response = await fetch(url);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const blob = await response.blob();
        const blobUrl = URL.createObjectURL(blob);

        const link = document.createElement('a');
        link.href = blobUrl;
        link.download = filename;
        link.style.display = 'none';

        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        // Clean up blob URL after a delay
        setTimeout(() => URL.revokeObjectURL(blobUrl), 1000);

        return true;
    } catch (error) {
        console.error('Download failed:', filename, error);
        // Fallback: open in new tab
        window.open(url, '_blank');
        return false;
    }
}

/**
 * Download a text file from base64 content
 */
function downloadFile(filename, base64Content, mimeType) {
    const link = document.createElement('a');
    link.href = `data:${mimeType};base64,${base64Content}`;
    link.download = filename;
    link.style.display = 'none';

    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
