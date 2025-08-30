export const sendMessageToBackend = (method: string, parameters?: any) => {
  const message = { Method: method, Parameters: parameters };
  if ((window as any).external && typeof (window as any).external.sendMessage === 'function') {
    const messageString = JSON.stringify(message);
    (window as any).external.sendMessage(messageString);
  } else {
    console.error('window.external.sendMessage is not available.');
  }
};
