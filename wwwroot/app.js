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

/**
 * Download multiple files as a single ZIP
 * @param {string} zipFilename - Name of the output ZIP file
 * @param {Array<{url: string, filename: string}>} files - Array of files to download
 */
async function downloadAsZip(zipFilename, files) {
    if (typeof JSZip === 'undefined') {
        throw new Error('JSZip library not loaded');
    }

    console.log(`Starting ZIP download: ${files.length} files`);

    const zip = new JSZip();
    const total = files.length;
    let completed = 0;
    let failed = 0;

    // Fetch all files in parallel (with concurrency limit)
    const concurrencyLimit = 4;
    const results = [];

    for (let i = 0; i < files.length; i += concurrencyLimit) {
        const batch = files.slice(i, i + concurrencyLimit);
        const batchPromises = batch.map(async (file) => {
            try {
                console.log(`Fetching: ${file.filename}`);
                const response = await fetch(file.url);
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                const blob = await response.blob();
                completed++;
                console.log(`Downloaded ${completed}/${total}: ${file.filename}`);
                return { filename: file.filename, blob, success: true };
            } catch (error) {
                console.error(`Failed to fetch ${file.filename}:`, error);
                failed++;
                completed++;
                return { filename: file.filename, success: false };
            }
        });

        const batchResults = await Promise.all(batchPromises);
        results.push(...batchResults);
    }

    // Add successful downloads to ZIP
    let addedCount = 0;
    for (const result of results) {
        if (result.success && result.blob) {
            zip.file(result.filename, result.blob);
            addedCount++;
        }
    }

    console.log(`Creating ZIP with ${addedCount} files...`);

    // Generate ZIP
    const zipBlob = await zip.generateAsync({
        type: 'blob',
        compression: 'STORE' // No compression for video files (already compressed)
    });

    console.log(`ZIP created: ${(zipBlob.size / 1024 / 1024).toFixed(2)} MB`);

    // Trigger download
    const blobUrl = URL.createObjectURL(zipBlob);
    const link = document.createElement('a');
    link.href = blobUrl;
    link.download = zipFilename;
    link.style.display = 'none';

    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    setTimeout(() => URL.revokeObjectURL(blobUrl), 5000);

    return {
        success: addedCount,
        failed: failed,
        total: total
    };
}
