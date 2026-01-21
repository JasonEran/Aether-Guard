from pathlib import Path

import torch

from model import LSTMPredictor


def main() -> None:
    model = LSTMPredictor()
    model_path = Path(__file__).resolve().parent / "model.pth"
    model_path.parent.mkdir(parents=True, exist_ok=True)
    torch.save(model.state_dict(), model_path)
    print(f"Initialized model weights at {model_path}")


if __name__ == "__main__":
    main()
